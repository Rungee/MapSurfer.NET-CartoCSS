﻿//==========================================================================================
//
//		MapSurfer.Styling.Formats.CartoCSS
//		Copyright (c) 2008-2016, MapSurfer.NET
//
//    Authors: Maxim Rylov
// 
//    A C# port of the carto library written by Mapbox (https://github.com/mapbox/carto/)
//    and released under the Apache License Version 2.0.
//
//==========================================================================================

//
// Here in, the parsing rules/functions
//
// The basic structure of the syntax tree generated is as follows:
//
//   Ruleset ->  Rule -> Value -> Expression -> Entity
//
// Here's some LESS code:
//
//    .class {
//      color: #fff;
//      border: 1px solid #000;
//      width: @w + 4px;
//      > .child {...}
//    }
//
// And here's what the parse tree might look like:
//
//     Ruleset (Selector '.class', [
//         Rule ("color",  Value ([Expression [Color #fff]]))
//         Rule ("border", Value ([Expression [Number 1px][Keyword "solid"][Color #000]]))
//         Rule ("width",  Value ([Expression [Operation "+" [Variable "@w"][Number 4px]]]))
//         Ruleset (Selector [Element '>', '.child'], [...])
//     ])
//
//  In general, most rules will try to parse a token with the `$()` function, and if the return
//  value is truly, will return a new node, of the relevant type. Sometimes, we need to check
//  first, before parsing, that's when we use `peek()`.
//


#pragma warning disable 665
// ReSharper disable RedundantNameQualifier

namespace MapSurfer.Styling.Formats.CartoCSS.Parser
{
  using MapSurfer.Styling.Formats.CartoCSS.Parser.Tree;
  using MapSurfer.Styling.Formats.CartoCSS.Parser.Infrastructure;
  using dotless.Core.Parser;
  using System.Collections.Generic;
  using dotless.Core.Exceptions;
  using dotless.Core.Parser.Infrastructure;
  using dotless.Core.Parser.Infrastructure.Nodes;
  using dotless.Core.Parser.Tree;
  using System;
  using System.Linq;
  using System.Text.RegularExpressions;

  internal class CartoParsers
  {
    public INodeProvider NodeProvider { get; set; }
    public Env Env { get; set; }

    private Regex _attributeNameRegex = new Regex("(\"\\[(.*?)\\]\")|\'\\[(.*?)\\]\'", RegexOptions.Compiled);
      
    public CartoParsers(INodeProvider nodeProvider, Env env)
    {
      NodeProvider = nodeProvider;
      Env = env;
    }

    //
    // The `primary` rule is the *entry* and *exit* point of the parser.
    // The rules here can appear at any level of the parse tree.
    //
    // The recursive nature of the grammar is an interplay between the `block`
    // rule, which represents `{ ... }`, the `ruleset` rule, and this `primary` rule,
    // as represented by this simplified grammar:
    //
    //     primary  →  (ruleset | rule)+
    //     ruleset  →  selector+ block
    //     block    →  '{' primary '}'
    //
    // Only at one point is the primary rule not called from the
    // block rule: at the root level.
    //
    public NodeList Primary(CartoParser parser)
    {
      Node node;
      var root = new NodeList();
      NodeList comments = null;

      GatherComments(parser);

      while (node = MixinDefinition(parser) || Rule(parser) || PullComments() || Ruleset(parser) || parser.Tokenizer.Match(@"/^[\s\n]+/") ||
                    MixinCall(parser) || Directive(parser) || (node = Invalid(parser)))
      {
        if (comments = PullComments())
        {
          root.AddRange(comments);
        }

        comments = node as NodeList;
        if (comments)
        {
          foreach (Comment c in comments)
          {
            c.IsPreSelectorComment = true;
          }
          root.AddRange(comments);
        }
        else
          root.Add(node);

        GatherComments(parser);
      }
      return root;
    }

    private NodeList CurrentComments { get; set; }

    /// <summary>
    ///  Gathers the comments and put them on the stack
    /// </summary>
    private void GatherComments(CartoParser parser)
    {
      Comment comment;
      while (comment = Comment(parser))
      {
        if (CurrentComments == null)
        {
          CurrentComments = new NodeList();
        }
        CurrentComments.Add(comment);
      }
    }

    /// <summary>
    ///  Collects comments from the stack retrived when gathering comments
    /// </summary>
    private NodeList PullComments()
    {
      NodeList comments = CurrentComments;
      CurrentComments = null;
      return comments;
    }

    /// <summary>
    ///  The equivalent of gathering any more comments and pulling everything on the stack
    /// </summary>
    private NodeList GatherAndPullComments(CartoParser parser)
    {
      GatherComments(parser);
      return PullComments();
    }

    private Stack<NodeList> CommentsStack = new Stack<NodeList>();

    /// <summary>
    ///  Pushes comments on to a stack for later use
    /// </summary>
    private void PushComments()
    {
      CommentsStack.Push(PullComments());
    }

    /// <summary>
    ///  Pops the comments stack
    /// </summary>
    private void PopComments()
    {
      CurrentComments = CommentsStack.Pop();
    }

    // We create a Comment node for CSS comments `/* */`,
    // but keep the LeSS comments `//` silent, by just skipping
    // over them.
    public Comment Comment(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;
      string comment = parser.Tokenizer.GetComment();

      if (comment != null)
      {
        return NodeProvider.Comment(comment, parser.Tokenizer.GetNodeLocation(index));
      }

      return null;
    }

    //
    // Entities are tokens which can be found inside an Expression
    //

    //
    // A string, which supports escaping " and '
    //
    //     "milky way" 'he\'s the one!'
    //
    public CartoQuotedNode Quoted(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;
      var escaped = false;
      var quote = parser.Tokenizer.CurrentChar;

      if (parser.Tokenizer.CurrentChar == '~')
      {
        escaped = true;
        quote = parser.Tokenizer.NextChar;
      }
      if (quote != '"' && quote != '\'')
        return null;

      if (escaped)
        parser.Tokenizer.Match('~');

      string str = parser.Tokenizer.GetQuotedString();

      if (str == null)
        return null;

      Quoted q = null;
      if (quote == '"' && _attributeNameRegex.IsMatch(str))
      {
        q = NodeProvider.Quoted(str, str.Trim('"'), escaped, parser.Tokenizer.GetNodeLocation(index));
        q.Quote = null;
      } else if (quote == '\'' && _attributeNameRegex.IsMatch(str))
      {
        q = NodeProvider.Quoted(str, str.Trim('\''), escaped, parser.Tokenizer.GetNodeLocation(index));
        q.Quote = null;
      }
      else
      {
        if (str == "\"''\"")
          str = "\"\"";
        q = NodeProvider.Quoted(str, str.Substring(1, str.Length - 2), escaped, parser.Tokenizer.GetNodeLocation(index));
       // if (q.Quote == '\'') 
       //   q.Quote = '\"';
      }

      return new CartoQuotedNode(q);
    }

    //
    // A catch-all word, such as:
    //
    //     black border-collapse
    //
    public Keyword Keyword(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;

      var k = parser.Tokenizer.Match(@"[A-Za-z-]+");
      if (k)
        return NodeProvider.Keyword(k.Value, parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // A function call
    //
    //     rgb(255, 0, 255)
    //
    // We also try to catch IE's `alpha()`, but let the `alpha` parser
    // deal with the details.
    //
    // The arguments are parsed with the `entities.arguments` parser.
    //
    public Call Call(CartoParser parser)
    {
      var memo = Remember(parser);
      var index = parser.Tokenizer.Location.Index;

      var name = parser.Tokenizer.Match(@"(%|[a-zA-Z0-9_-]+|progid:[\w\.]+)\(");

      if (!name)
        return null;

      if (name[1].ToLowerInvariant() == "alpha")
      {
        var alpha = Alpha(parser);
        if (alpha != null)
          return alpha;
      }

      var args = Arguments(parser);

      if (!parser.Tokenizer.Match(')'))
      {
        Recall(parser, memo);
        return null;
      }

      return NodeProvider.Call(name[1], args, parser.Tokenizer.GetNodeLocation(index));
    }

    public NodeList<Node> Arguments(CartoParser parser)
    {
      var args = new NodeList<Node>();
      Node arg;

      while ((arg = Assignment(parser)) || (arg = Expression(parser)))
      {
        args.Add(arg);
        if (!parser.Tokenizer.Match(','))
          break;
      }
      return args;
    }

    // Assignments are argument entities for calls.
    // They are present in ie filter properties as shown below.
    //
    //     filter: progid:DXImageTransform.Microsoft.Alpha( *opacity=50* )	
    //
    public Assignment Assignment(CartoParser parser)
    {
      var key = parser.Tokenizer.Match(@"\w+(?=\s?=)");

      if (!key || !parser.Tokenizer.Match('='))
      {
        return null;
      }

      var value = Entity(parser);

      if (value)
      {
        return NodeProvider.Assignment(key.Value, value, key.Location);
      }

      return null;
    }

    public Node Literal(CartoParser parser)
    {
      return Dimension(parser) ||
             Color(parser) ||
             HexColor(parser) ||
             Quoted(parser);
    }

    public Node HexColor(CartoParser parser)
    {
      RegexMatchResult rgb;

      if (parser.Tokenizer.CurrentChar == '#' && (rgb = parser.Tokenizer.Match(@"@\[a-fA-F0-9]{6}|[a-fA-F0-9]{3})")))
        return new dotless.Core.Parser.Tree.Color(rgb.Value);

      return null;
    }

    //
    // Parse url() tokens
    //
    // We use a specific rule for urls, because they don't really behave like
    // standard function calls. The difference is that the argument doesn't have
    // to be enclosed within a string, so it can't be parsed as an Expression.
    //
    public Url Url(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;

      if (parser.Tokenizer.CurrentChar != 'u' || !parser.Tokenizer.Match(@"url\("))
        return null;

      GatherComments(parser);

      Node value = Quoted(parser) || Variable(parser) || parser.Tokenizer.Match(@"^[\-\w%@$\/.&=:;#+?~]+"); // '';

      if (value)
      {
        value.PreComments = PullComments();
        value.PostComments = GatherAndPullComments(parser);
      }

      Expect(parser, ')');

      if (value == null)
        return NodeProvider.Url(new Quoted(string.Empty, true), parser.Importer, parser.Tokenizer.GetNodeLocation(index));
      else
        return NodeProvider.Url(value is Variable ? value : new Quoted(value.ToCSS(Env), false), parser.Importer, parser.Tokenizer.GetNodeLocation(index));
    }

    //
    // A Variable entity, such as `@fink`, in
    //
    //     width: @fink + 2px
    //
    // We use a different parser for variable definitions,
    // see `parsers.variable`.
    //
    public Variable Variable(CartoParser parser)
    {
      RegexMatchResult name;
      var index = parser.Tokenizer.Location.Index;

      if (parser.Tokenizer.CurrentChar == '@' && (name = parser.Tokenizer.Match(@"@(@?[a-zA-Z0-9_-]+)")))
        return NodeProvider.Variable(name.Value, parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // A Variable entity as like in a selector e.g.
    //
    //     @{var} {
    //     }
    //
    public Variable VariableCurly(CartoParser parser)
    {
      RegexMatchResult name;
      var index = parser.Tokenizer.Location.Index;

      if (parser.Tokenizer.CurrentChar == '@' && (name = parser.Tokenizer.Match(@"@\{([a-zA-Z0-9_-]+)\}")))
        return NodeProvider.Variable("@" + name.Match.Groups[1].Value, parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // A Hexadecimal color
    //
    //     #4F3C2F
    //
    // `rgb` and `hsl` colors are parsed through the `entities.call` parser.
    //
    public Color Color(CartoParser parser)
    {
      RegexMatchResult hex;

      var index = parser.Tokenizer.Location.Index;

      if (parser.Tokenizer.CurrentChar == '#' &&
          (hex = parser.Tokenizer.Match(@"#([a-fA-F0-9]{8}|[a-fA-F0-9]{6}|[a-fA-F0-9]{3})")))
        return NodeProvider.Color(hex[1], parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // A Dimension, that is, a number and a unit
    //
    //     0.5em 95%
    //
    public CartoDimension Dimension(CartoParser parser)
    {
      var c = parser.Tokenizer.CurrentChar;
      if ((c > 57 || c < 45) || c == 47)
        return null;

      var index = parser.Tokenizer.Location.Index;

      var value = parser.Tokenizer.Match(@"^(-?\d*\.?\d+(?:[eE][-+]?\d+)?)(\%|\w+)?");

      if (value)
        return new CartoDimension(value[1], value[2], parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // C# code to be evaluated
    //
    //     ``
    //
    public Script Script(CartoParser parser)
    {
      if (parser.Tokenizer.CurrentChar != '`')
        return null;

      var index = parser.Tokenizer.Location.Index;

      var script = parser.Tokenizer.MatchAny(@"`[^`]*`");

      if (!script)
      {
        return null;
      }

      return NodeProvider.Script(script.Value, parser.Tokenizer.GetNodeLocation(index));
    }


    //
    // The variable part of a variable definition. Used in the `rule` parser
    //
    //     @fink:
    //
    public string VariableName(CartoParser parser)
    {
      var variable = Variable(parser);

      if (variable != null)
        return variable.Name;

      return null;
    }

    //
    // A font size/line-height shorthand
    //
    //     small/12px
    //
    // We need to peek first, or we'll match on keywords and dimensions
    //
    public Shorthand Shorthand(CartoParser parser)
    {
      if (!parser.Tokenizer.Peek(@"[@%\w.-]+\/[@%\w.-]+"))
        return null;

      var index = parser.Tokenizer.Location.Index;

      Node a = null;
      Node b = null;
      if ((a = Entity(parser)) && parser.Tokenizer.Match('/') && (b = Entity(parser)))
        return NodeProvider.Shorthand(a, b, parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // Mixins
    //

    //
    // A Mixin call, with an optional argument list
    //
    //     #mixins > .square(#fff);
    //     .rounded(4px, black);
    //     .button;
    //
    // The `while` loop is there because mixins can be
    // namespaced, but we only support the child and descendant
    // selector for now.
    //
    public MixinCall MixinCall(CartoParser parser)
    {
      var elements = new NodeList<Element>();
      var index = parser.Tokenizer.Location.Index;
      bool important = false;

      RegexMatchResult e;
      Combinator c = null;

      PushComments();

      for (var i = parser.Tokenizer.Location.Index; e = parser.Tokenizer.Match(@"[#.][a-zA-Z0-9_-]+"); i = parser.Tokenizer.Location.Index)
      {
        elements.Add(NodeProvider.Element(c, e, parser.Tokenizer.GetNodeLocation(index)));

        i = parser.Tokenizer.Location.Index;
        var match = parser.Tokenizer.Match('>');
        c = match != null ? NodeProvider.Combinator(match.Value, parser.Tokenizer.GetNodeLocation(index)) : null;
      }

      if (elements.Count == 0)
      {
        PopComments();
        return null;
      }

      var args = new List<NamedArgument>();
      if (parser.Tokenizer.Match('('))
      {
        Expression arg;
        while (arg = Expression(parser))
        {
          var value = arg;
          string name = null;

          if (arg.Value.Count == 1 && arg.Value[0] is Variable)
          {
            if (parser.Tokenizer.Match(':'))
            {
              value = Expect(Expression(parser), "expected value", parser);
              name = (arg.Value[0] as Variable).Name;
            }
          }

          args.Add(new NamedArgument { Name = name, Value = value });

          if (!parser.Tokenizer.Match(','))
            break;
        }
        Expect(parser, ')');
      }

      GatherComments(parser);

      if (!string.IsNullOrEmpty(Important(parser)))
      {
        important = true;
      }

      // if elements then we've picked up chars so don't need to worry about remembering
      var postComments = GatherAndPullComments(parser);

      if (End(parser))
      {
        var mixinCall = NodeProvider.MixinCall(elements, args, important, parser.Tokenizer.GetNodeLocation(index));
        mixinCall.PostComments = postComments;
        PopComments();
        return mixinCall;
      }

      PopComments();
      return null;
    }

    //
    // A Mixin definition, with a list of parameters
    //
    //     .rounded (@radius: 2px, @color) {
    //        ...
    //     }
    //
    // Until we have a finer grained state-machine, we have to
    // do a look-ahead, to make sure we don't have a mixin call.
    // See the `rule` function for more information.
    //
    // We start by matching `.rounded (`, and then proceed on to
    // the argument list, which has optional default values.
    // We store the parameters in `params`, with a `value` key,
    // if there is a value, such as in the case of `@radius`.
    //
    // Once we've got our params list, and a closing `)`, we parse
    // the `{...}` block.
    //
    public MixinDefinition MixinDefinition(CartoParser parser)
    {
      if ((parser.Tokenizer.CurrentChar != '.' && parser.Tokenizer.CurrentChar != '#') ||
          parser.Tokenizer.Peek(@"[^{]*(;|})"))
        return null;

      var index = parser.Tokenizer.Location.Index;

      var memo = Remember(parser);

      var match = parser.Tokenizer.Match(@"([#.](?:[\w-]|\\(?:[a-fA-F0-9]{1,6} ?|[^a-fA-F0-9]))+)\s*\(");
      if (!match)
        return null;

      //mixin definition ignores comments before it - a css hack can't be part of a mixin definition,
      //so it may as well be a rule before the definition
      PushComments();
      GatherAndPullComments(parser); // no store as mixin definition not output

      var name = match[1];
      bool variadic = false;
      var parameters = new NodeList<Rule>();
      RegexMatchResult param = null;
      Node param2 = null;
      Condition condition = null;
      int i;
      while (true)
      {
        i = parser.Tokenizer.Location.Index;
        if (parser.Tokenizer.CurrentChar == '.' && parser.Tokenizer.Match("\\.{3}"))
        {
          variadic = true;
          break;
        }

        if (param = parser.Tokenizer.Match(@"@[a-zA-Z0-9_-]+"))
        {
          GatherAndPullComments(parser);

          if (parser.Tokenizer.Match(':'))
          {
            GatherComments(parser);
            var value = Expect(Expression(parser), "Expected value", parser);

            parameters.Add(NodeProvider.Rule(param.Value, value, parser.Tokenizer.GetNodeLocation(i)));
          }
          else if (parser.Tokenizer.Match("\\.{3}"))
          {
            variadic = true;
            parameters.Add(NodeProvider.Rule(param.Value, null, true, parser.Tokenizer.GetNodeLocation(i)));
            break;
          }
          else
            parameters.Add(NodeProvider.Rule(param.Value, null, parser.Tokenizer.GetNodeLocation(i)));

        }
        else if (param2 = Literal(parser) || Keyword(parser))
        {
          parameters.Add(NodeProvider.Rule(null, param2, parser.Tokenizer.GetNodeLocation(i)));
        }
        else
          break;

        GatherAndPullComments(parser);

        if (!parser.Tokenizer.Match(','))
          break;

        GatherAndPullComments(parser);
      }

      if (!parser.Tokenizer.Match(')'))
      {
        Recall(parser, memo);
      }

      GatherAndPullComments(parser);

      if (parser.Tokenizer.Match("when"))
      {
        GatherAndPullComments(parser);

        condition = Expect(Conditions(parser), "Expected conditions after when (mixin guards)", parser);
      }

      var rules = Block(parser);

      PopComments();

      if (rules != null)
        return NodeProvider.MixinDefinition(name, parameters, rules, condition, variadic, parser.Tokenizer.GetNodeLocation(index));

      Recall(parser, memo);

      return null;
    }

    /// <summary>
    ///  a list of , seperated conditions (, == OR)
    /// </summary>
    public Condition Conditions(CartoParser parser)
    {
      Condition condition, nextCondition;

      if (condition = Condition(parser))
      {
        while (parser.Tokenizer.Match(','))
        {
          nextCondition = Expect(Condition(parser), ", without recognised condition", parser);

          condition = NodeProvider.Condition(condition, "or", nextCondition, false, parser.Tokenizer.GetNodeLocation());
        }
        return condition;
      }

      return null;
    }

    /// <summary>
    ///  A condition is used for mixin definitions and is made up
    ///  of left operation right
    /// </summary>
    public Condition Condition(CartoParser parser)
    {
      int index = parser.Tokenizer.Location.Index;
      bool negate = false;
      Condition condition;
      //var a, b, c, op, index = i, negate = false;

      if (parser.Tokenizer.Match("not"))
      {
        negate = true;
      }

      Expect(parser, '(');

      Node left = Expect(Addition(parser) || Keyword(parser) || Quoted(parser), "unrecognised condition", parser);

      var op = parser.Tokenizer.Match("(>=|=<|[<=>])");

      if (op)
      {
        Node right = Expect(Addition(parser) || Keyword(parser) || Quoted(parser), "unrecognised right hand side condition expression", parser);

        condition = NodeProvider.Condition(left, op.Value, right, negate, parser.Tokenizer.GetNodeLocation(index));
      }
      else
      {
        condition = NodeProvider.Condition(left, "=", NodeProvider.Keyword("true", parser.Tokenizer.GetNodeLocation(index)), negate, parser.Tokenizer.GetNodeLocation(index));
      }

      Expect(parser, ')');

      if (parser.Tokenizer.Match("and"))
      {
        return NodeProvider.Condition(condition, "and", Condition(parser), false, parser.Tokenizer.GetNodeLocation(index));
      }

      return condition;
    }

    //
    // Entities are the smallest recognized token,
    // and can be found inside a rule's value.
    //
    public Node Entity(CartoParser parser)
    {
      return Literal(parser) || Field(parser) || Variable(parser) || Url(parser) ||
             Call(parser) || Keyword(parser) || Script(parser);
    }

    //
    // A Rule terminator. Note that we use `Peek()` to check for '}',
    // because the `block` rule will be expecting it, but we still need to make sure
    // it's there, if ';' was ommitted.
    //
    public bool End(CartoParser parser)
    {
      return parser.Tokenizer.Match(';') || parser.Tokenizer.Peek('}');
    }

    //
    // IE's alpha function
    //
    //     alpha(opacity=88)
    //
    public Alpha Alpha(CartoParser parser)
    {
      Node value;

      var index = parser.Tokenizer.Location.Index;

      if (!parser.Tokenizer.Match(@"opacity=", true))
        return null;

      if (value = parser.Tokenizer.Match(@"[0-9]+") || Variable(parser))
      {
        Expect(parser, ')');

        return NodeProvider.Alpha(value, parser.Tokenizer.GetNodeLocation(index));
      }

      return null;
    }

    //
    // A Selector Element
    //
    //     div
    //     + h1
    //     #socks
    //     input[type="text"]
    //
    // Elements are the building blocks for Selectors,
    // they are made out of a `Combinator` (see combinator rule),
    // and an element name, such as a tag a class, or `*`.
    //
    public Element SelectorElement(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;

      GatherComments(parser);

      Combinator c = Combinator(parser);

      PushComments();
      GatherComments(parser); // to collect, combinator must have picked up something which would require memory anyway
                              // Node e = parser.Tokenizer.Match(@"[.#:]?(\\.|[a-zA-Z0-9_-])+") || parser.Tokenizer.Match('*') || parser.Tokenizer.Match('&') || Zoom(parser) || Filter(parser) || Attachment(parser) ||
                              //     Attribute(parser) || parser.Tokenizer.MatchAny(@"\([^)@]+\)") || parser.Tokenizer.Match(@"[\.#](?=@\{)") || VariableCurly(parser);
                              // Node e = Element2(parser) || Zoom(parser) || Filter(parser) || Attachment(parser);
                              //Node e, z, f, a;
                              //bool b = false;
                              //if (e = Element(parser) || (z = Zoom(parser)) || (f = Filter(parser)) || (a = Attachment(parser)) || VariableCurly(parser))
                              //{
                              //  b = true;
                              //}

      Node e = Element(parser) || Zoom(parser) || Filter(parser) || Attachment(parser) || VariableCurly(parser);

      if (!e)
      {
        if (parser.Tokenizer.Match('('))
        {
          var variable = Variable(parser) ?? VariableCurly(parser);
          if (variable)
          {
            parser.Tokenizer.Match(')');
            e = NodeProvider.Paren(variable, parser.Tokenizer.GetNodeLocation(index));
          }
        }
      }

      if (e)
      {
        c.PostComments = PullComments();
        PopComments();
        c.PreComments = PullComments();

        Element elem = e as Element;
        if (elem != null)
        {
          elem.Location = parser.Tokenizer.GetNodeLocation(index);
          return elem;
        }
        else
        {
          return NodeProvider.Element(c, e, parser.Tokenizer.GetNodeLocation(index));
        }
      }

      PopComments();
      return null;
    }


    public CartoElement Element(CartoParser parser)
    {
      Node e = parser.Tokenizer.Match(@"^(?:[.#][\w\-]+|\*|Map)");
      if (e)
      {
        return new CartoElement(Combinator(parser), e);
      }

      return null;
    }

    public CartoAttachmentElement Attachment(CartoParser parser)
    {
      RegexMatchResult match = parser.Tokenizer.Match(@"^::([\w\-]+(?:\/[\w\-]+)*)");

      if (match)
      {
        return new CartoAttachmentElement(Combinator(parser), match, match[1]);
      }

      return null;
    }

    public CartoZoomElement Zoom(CartoParser parser)
    {
      Node e = parser.Tokenizer.Match(@"(?<=\s+|^)\[zoom");

      if (e)
      {
        Node comp = Comparison(parser);

        if (comp != null)
        {
          Node value = Variable(parser) || Dimension(parser);

          if (value != null && parser.Tokenizer.CurrentChar == ']')
          {
            parser.Tokenizer.Advance(1);
            return new CartoZoomElement(comp, value, Combinator(parser), e);
          }
        }
      }

      return null;
    }

    public CartoFilterElement Filter(CartoParser parser)
    {
      Node key, op, value = null;
      /*if (! $('[')) return;
                if (key = $(/^[a-zA-Z0-9\-_]+/) ||
                    $(this.entities.quoted) ||
                    $(this.entities.variable) ||
                    $(this.entities.keyword) ||
                    $(this.entities.field)) {
                    // TODO: remove at 1.0.0
                    if (key instanceof tree.Quoted) {
                        key = new tree.Field(key.toString());
                    }
                    if ((op = $(this.entities.comparison)) &&
                        (val = $(this.entities.quoted) ||
                             $(this.entities.variable) ||
                             $(this.entities.dimension) ||
                             $(this.entities.keyword) ||
                             $(this.entities.field))) {
                        if (! $(']')) {
                            throw makeError({
                                message: 'Missing closing ] of filter.',
                                index: memo - 1
                            });
                        }
                        if (!key.is) key = new tree.Field(key);
                        return new tree.Filter(key, op, val, memo, env.filename);
                    }
                }*/

      if (!IsChar(parser, '['))
        return null;

      parser.Tokenizer.Advance(1);

      if (key = parser.Tokenizer.Match(@"^[a-zA-Z0-9\-_]+") || Quoted(parser) || Variable(parser) || Keyword(parser))
      {
        if (key is Quoted)
        {
          key = new CartoFieldNode(key.ToString());
        }

        if ((op = Comparison(parser)) && (value = Quoted(parser) || Variable(parser) || Dimension(parser) || Keyword(parser)))
        {
          if (!IsChar(parser, ']'))
          {
            throw new ParserException("Missing closing ']' in filter");
          }

          parser.Tokenizer.Advance(1);

          if (key != null)
            key = new CartoFieldNode(key.ToString());

          return new CartoFilterElement(key, op, value, Combinator(parser), Env);
        }
      }

      return null;
    }

    public CartoFieldNode Field(CartoParser parser)
    {
      if (!IsChar(parser, '['))
        return null;

      RegexMatchResult match = parser.Tokenizer.Match(@"\[([^]]+)\]");

      //if (!IsChar(parser, ']'))
      //  return null;

      if (match != null)
        return new CartoFieldNode("[" + match[1] + "]");

      return null;
    }

    public bool IsChar(CartoParser parser, char ch)
    {
      return parser.Tokenizer.CurrentChar == ch;
    }

    public Node Comparison(CartoParser parser)
    {
      RegexMatchResult comp;

      var index = parser.Tokenizer.Location.Index;

      if (comp = parser.Tokenizer.Match(@"^=~|=|!=|<=|>=|<|>|%"))
        return NodeProvider.TextNode(comp.ToString(), parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // Combinators combine elements together, in a Selector.
    //
    // Because our parser isn't white-space sensitive, special care
    // has to be taken, when parsing the descendant combinator, ` `,
    // as it's an empty space. We have to check the previous character
    // in the input, to see if it's a ` ` character. More info on how
    // we deal with this in *combinator.js*.
    //
    public Combinator Combinator(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;

      Node match;
      //if (match = parser.Tokenizer.Match(@"[+>~]") || parser.Tokenizer.Match(@"::"))
      if (match = parser.Tokenizer.Match(@"[+>~]"))  // Runge
        return NodeProvider.Combinator(match.ToString(), parser.Tokenizer.GetNodeLocation(index));

      return NodeProvider.Combinator(char.IsWhiteSpace(parser.Tokenizer.GetPreviousCharIgnoringComments()) ? " " : null, parser.Tokenizer.GetNodeLocation(index));
    }

    //
    // A CSS Selector
    //
    //     .class > div + h1
    //     li a:hover
    //
    // Selectors are made out of one or more Elements, see above.
    //
    public Selector Selector(CartoParser parser)
    {
      Element e;
      int realElements = 0;

      var elements = new NodeList<Element>();
      var index = parser.Tokenizer.Location.Index;

      GatherComments(parser);
      PushComments();

      if (parser.Tokenizer.Match('('))
      {
        var sel = Entity(parser);
        Expect(parser, ')');
        return NodeProvider.Selector(new NodeList<Element>() { NodeProvider.Element(null, sel, parser.Tokenizer.GetNodeLocation(index)) }, parser.Tokenizer.GetNodeLocation(index));
      }

      while (true)
      {
        e = SelectorElement(parser);
        if (!e)
          break;

        realElements++;
        elements.Add(e);

        char ch = parser.Tokenizer.CurrentChar;
        if (ch == '{' || ch == '}' || ch == ';' || ch == ',')
          break;
      }

      if (realElements > 0)
      {
        var selector = (NodeProvider as CartoNodeProvider).Selector(elements, parser.Tokenizer.GetNodeLocation(index), Env);
        selector.PostComments = GatherAndPullComments(parser);
        PopComments();
        selector.PreComments = PullComments();

        return selector;
      }

      PopComments();
      //We have lost comments we have absorbed here.
      //But comments should be absorbed before selectors...
      return null;
    }

    public Node Tag(CartoParser parser)
    {
      return parser.Tokenizer.Match(@"[a-zA-Z][a-zA-Z-]*[0-9]?") || parser.Tokenizer.Match('*');
    }

    public TextNode Attribute(CartoParser parser)
    {
      var attr = "";
      Node key;
      Node val = null;

      var index = parser.Tokenizer.Location.Index;

      if (!parser.Tokenizer.Match('['))
        return null;

      if (key = parser.Tokenizer.Match(@"(\\.|[a-zA-Z0-9_-])+") || Quoted(parser))
      {
        Node op;
        if ((op = parser.Tokenizer.Match(@"[|~*$^]?=")) &&
            (val = Quoted(parser) || parser.Tokenizer.Match(@"[\w-]+")))
          // Would be nice if this wasn't one block - we could make Attribute node
          // see CommentsInSelectorAttributes in CommentsFixture.cs
          attr = string.Format("{0}{1}{2}", key, op, val.ToCSS(Env));
        else
          attr = key.ToString();
      }

      Expect(parser, ']');

      if (!string.IsNullOrEmpty(attr))
        return NodeProvider.TextNode("[" + attr + "]", parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

    //
    // The `block` rule is used by `ruleset` and `mixin.definition`.
    // It's a wrapper around the `primary` rule, with added `{}`.
    //
    public NodeList Block(CartoParser parser)
    {
      if (!parser.Tokenizer.Match('{'))
        return null;

      var content = Expect(Primary(parser), "Expected content inside block", parser);

      Expect(parser, '}');

      return content;
    }

    //
    // div, .class, body > p {...}
    //
    public Ruleset Ruleset(CartoParser parser)
    {
      var selectors = new NodeList<Selector>();

      var memo = Remember(parser);
      var index = memo.TokenizerLocation.Index;

      /* if (parser.Tokenizer.Peek(@"([a-z.#: _-]+)[\s\n]*\{")) //simple case with no comments
       {
         var match = parser.Tokenizer.Match(@"[a-z.#: _-]+");
         var s = NodeProvider.Selector(new NodeList<Element>(NodeProvider.Element(null, match, parser.Tokenizer.GetNodeLocation(index))), parser.Tokenizer.GetNodeLocation(index));
         selectors = new NodeList<Selector>(s);
       }
       else*/ // Runge
      {
        Selector s;
        while (s = Selector(parser))
        {
          selectors.Add(s);
          if (!parser.Tokenizer.Match(','))
            break;

          GatherComments(parser);
        }
      }

      NodeList rules;

      if (selectors.Count > 0 && (rules = Block(parser)) != null)
      {
        return NodeProvider.Ruleset(selectors, rules, parser.Tokenizer.GetNodeLocation(index));
      }

      Recall(parser, memo);

      return null;
    }

    public CartoInvalidElement Invalid(CartoParser parser)
    {
      NodeLocation loc = parser.Tokenizer.GetNodeLocation();
      var match = parser.Tokenizer.Match("^[^;\n]*[;\n]");

      // To fail gracefully, match everything until a semicolon or linebreak.
      if (match != null)
      {
        CartoInvalidElement cie = new CartoInvalidElement(Combinator(parser), match.Value);
        cie.Location = loc;
        return cie;
      }

      return null;
    }

    public dotless.Core.Parser.Tree.Rule Rule(CartoParser parser)
    {
      var memo = Remember(parser);
      PushComments();

      char ch = parser.Tokenizer.CurrentChar;

      if (ch == '.' || ch == '#')
        return null;

      Node name, value;

      if ((name = Variable(parser) || Property(parser)) && parser.Tokenizer.Match(':'))
      {
        var postNameComments = GatherAndPullComments(parser);

        var preValueComments = GatherAndPullComments(parser);

        value = Value(parser);

        var postValueComments = GatherAndPullComments(parser);

        if (End(parser))
        {
          if (value == null)
            throw new ParsingException(name + " is incomplete", parser.Tokenizer.GetNodeLocation());

          value.PreComments = preValueComments;
          value.PostComments = postValueComments;

          string nameValue = name is Variable ? (name as Variable).Name : (name as RegexMatchResult).Value;

          var rule = NodeProvider.Rule(nameValue, value, parser.Tokenizer.GetNodeLocation(memo.TokenizerLocation.Index));
          rule.PostNameComments = postNameComments;
          PopComments();
          return rule;
        }
      }

      PopComments();
      Recall(parser, memo);

      return null;
    }

    //
    // An @import directive
    //
    //     @import "lib";
    //
    // Depending on our environemnt, importing is done differently:
    // In the browser, it's an XHR request, in Node, it would be a
    // file-system operation. The function used for importing is
    // stored in `import`, which we pass to the Import constructor.
    //
    public Import Import(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;

      var importMatch = parser.Tokenizer.Match(@"@import(-(once))?\s+");
      if (!importMatch)
      {
        return null;
      }

      ImportOptions option = ParseOptions(parser);

      Node path = Quoted(parser) || Url(parser);
      if (!path)
      {
        return null;
      }

      var features = MediaFeatures(parser);

      Expect(parser, ';', "Expected ';' (possibly unrecognised media sequence)");

      if (path is Quoted)
        return NodeProvider.Import(path as Quoted, features, option, parser.Tokenizer.GetNodeLocation(index));

      if (path is Url)
        return NodeProvider.Import(path as Url, features, option, parser.Tokenizer.GetNodeLocation(index));

      throw new ParsingException("unrecognised @import format", parser.Tokenizer.GetNodeLocation(index));
    }

    private static ImportOptions ParseOptions(CartoParser parser)
    {
      var index = parser.Tokenizer.Location.Index;
      var optionsMatch = parser.Tokenizer.Match(@"\((?<keywords>.*)\)");
      if (!optionsMatch)
      {
        return ImportOptions.Once;
      }

      var allKeywords = optionsMatch.Match.Groups["keywords"].Value;
      var keywords = allKeywords.Split(',').Select(kw => kw.Trim());

      ImportOptions options = 0;
      foreach (var keyword in keywords)
      {
        try
        {
          ImportOptions value = (ImportOptions)Enum.Parse(typeof(ImportOptions), keyword, true);
          options |= value;
        }
        catch (ArgumentException)
        {
          throw new ParsingException(string.Format("unrecognized @import option '{0}'", keyword), parser.Tokenizer.GetNodeLocation(index));
        }
      }

      CheckForConflictingOptions(parser, options, allKeywords, index);

      return options;
    }
    private static readonly ImportOptions[][] illegalOptionCombinations =
             {
                new[] {ImportOptions.Css, ImportOptions.Less},
                new[] {ImportOptions.Inline, ImportOptions.Css},
                new[] {ImportOptions.Inline, ImportOptions.Less},
                new[] {ImportOptions.Inline, ImportOptions.Reference},
                new[] {ImportOptions.Once, ImportOptions.Multiple},
                new[] {ImportOptions.Reference, ImportOptions.Css},
            };
    private static void CheckForConflictingOptions(CartoParser parser, ImportOptions options, string allKeywords, int index)
    {
      foreach (var illegalCombination in illegalOptionCombinations)
      {
        if (IsOptionSet(options, illegalCombination[0]) && IsOptionSet(options, illegalCombination[1]))
        {
          throw new ParsingException(
              string.Format(
                  "invalid combination of @import options ({0}) -- specify either {1} or {2}, but not both",
                  allKeywords,
                  illegalCombination[0].ToString().ToLowerInvariant(),
                  illegalCombination[1].ToString().ToLowerInvariant()
                  ),
              parser.Tokenizer.GetNodeLocation(index));
        }
      }
    }

    private static bool IsOptionSet(ImportOptions options, ImportOptions test)
    {
      return (options & test) == test;
    }
    //
    // A CSS Directive
    //
    //     @charset "utf-8";
    //
    public Node Directive(CartoParser parser)
    {
      if (parser.Tokenizer.CurrentChar != '@')
        return null;

      var import = Import(parser);
      if (import)
        return import;

      var media = Media(parser);
      if (media)
        return media;

      GatherComments(parser);

      var index = parser.Tokenizer.Location.Index;

      var name = parser.Tokenizer.MatchString(@"@[-a-z]+");
      bool hasIdentifier = false, hasBlock = false, isKeyFrame = false;
      NodeList rules, preRulesComments = null, preComments = null;
      string identifierRegEx = @"[^{]+";
      string nonVendorSpecificName = name;

      if (name.StartsWith("@-") && name.IndexOf('-', 2) > 0)
      {
        nonVendorSpecificName = "@" + name.Substring(name.IndexOf('-', 2) + 1);
      }

      switch (nonVendorSpecificName)
      {
        case "@font-face":
          hasBlock = true;
          break;
        case "@page":
        case "@document":
        case "@supports":
          hasBlock = true;
          hasIdentifier = true;
          break;
        case "@viewport":
        case "@top-left":
        case "@top-left-corner":
        case "@top-center":
        case "@top-right":
        case "@top-right-corner":
        case "@bottom-left":
        case "@bottom-left-corner":
        case "@bottom-center":
        case "@bottom-right":
        case "@bottom-right-corner":
        case "@left-top":
        case "@left-middle":
        case "@left-bottom":
        case "@right-top":
        case "@right-middle":
        case "@right-bottom":
          hasBlock = true;
          break;
        case "@keyframes":
          isKeyFrame = true;
          hasIdentifier = true;
          break;
      }

      string identifier = "";

      preComments = PullComments();

      if (hasIdentifier)
      {
        GatherComments(parser);

        var identifierRegResult = parser.Tokenizer.MatchAny(identifierRegEx);
        if (identifierRegResult != null)
        {
          identifier = identifierRegResult.Value.Trim();
        }
      }

      preRulesComments = GatherAndPullComments(parser);

      if (hasBlock)
      {
        rules = Block(parser);

        if (rules != null)
        {
          rules.PreComments = preRulesComments;
          return NodeProvider.Directive(name, identifier, rules, parser.Tokenizer.GetNodeLocation(index));
        }
      }
      else if (isKeyFrame)
      {
        var keyframeblock = KeyFrameBlock(parser, name, identifier, index);
        keyframeblock.PreComments = preRulesComments;
        return keyframeblock;
      }
      else
      {
        Node value;
        if (value = Expression(parser))
        {
          value.PreComments = preRulesComments;
          value.PostComments = GatherAndPullComments(parser);
          if (parser.Tokenizer.Match(';'))
          {
            var directive = NodeProvider.Directive(name, value, parser.Tokenizer.GetNodeLocation(index));
            directive.PreComments = preComments;
            return directive;
          }
        }
      }

      throw new ParsingException("directive block with unrecognised format", parser.Tokenizer.GetNodeLocation(index));
    }

    public Expression MediaFeature(CartoParser parser)
    {
      NodeList features = new NodeList();
      var outerIndex = parser.Tokenizer.Location.Index;

      while (true)
      {
        GatherComments(parser);

        var keyword = Keyword(parser);
        if (keyword)
        {
          keyword.PreComments = PullComments();
          keyword.PostComments = GatherAndPullComments(parser);
          features.Add(keyword);
        }
        else if (parser.Tokenizer.Match('('))
        {
          GatherComments(parser);

          var memo = Remember(parser);
          var index = parser.Tokenizer.Location.Index;
          var property = PropertyValue(parser);

          var preComments = GatherAndPullComments(parser);

          // in order to support (color) and have rule/*comment*/: we need to keep :
          // out of property
          if (!string.IsNullOrEmpty(property) && !parser.Tokenizer.Match(':'))
          {
            Recall(parser, memo);
            property = null;
          }

          GatherComments(parser);

          memo = Remember(parser);

          var entity = Entity(parser);

          if (!entity || !parser.Tokenizer.Match(')'))
          {
            Recall(parser, memo);

            // match "3/2" for instance
            var unrecognised = parser.Tokenizer.Match(@"[^\){]+");
            if (unrecognised)
            {
              entity = NodeProvider.TextNode(unrecognised.Value, parser.Tokenizer.GetNodeLocation());
              Expect(parser, ')');
            }
          }

          if (!entity)
          {
            return null;
          }

          entity.PreComments = PullComments();
          entity.PostComments = GatherAndPullComments(parser);

          if (!string.IsNullOrEmpty(property))
          {
            var rule = NodeProvider.Rule(property, entity, parser.Tokenizer.GetNodeLocation(index));
            rule.IsSemiColonRequired = false;
            features.Add(NodeProvider.Paren(rule, parser.Tokenizer.GetNodeLocation(index)));
          }
          else
          {
            features.Add(NodeProvider.Paren(entity, parser.Tokenizer.GetNodeLocation(index)));
          }
        }
        else
        {
          break;
        }
      }

      if (features.Count == 0)
        return null;

      return NodeProvider.Expression(features, parser.Tokenizer.GetNodeLocation(outerIndex));
    }

    public Value MediaFeatures(CartoParser parser)
    {
      List<Node> features = new List<Node>();
      int index = parser.Tokenizer.Location.Index;

      while (true)
      {
        Node feature = MediaFeature(parser) || Variable(parser);
        if (!feature)
        {
          return null;
        }

        features.Add(feature);

        if (!parser.Tokenizer.Match(","))
          break;
      }

      return NodeProvider.Value(features, null, parser.Tokenizer.GetNodeLocation(index));
    }

    public Media Media(CartoParser parser)
    {
      if (!parser.Tokenizer.Match("@media"))
        return null;

      var index = parser.Tokenizer.Location.Index;

      var features = MediaFeatures(parser);

      var preRulesComments = GatherAndPullComments(parser);

      var rules = Expect(Block(parser), "@media block with unrecognised format", parser);

      rules.PreComments = preRulesComments;
      return NodeProvider.Media(rules, features, parser.Tokenizer.GetNodeLocation(index));
    }

    public Directive KeyFrameBlock(CartoParser parser, string name, string identifier, int index)
    {
      if (!parser.Tokenizer.Match('{'))
        return null;

      NodeList keyFrames = new NodeList();
      const string identifierRegEx = "from|to|([0-9\\.]+%)";

      while (true)
      {
        GatherComments(parser);

        NodeList keyFrameElements = new NodeList();

        while (true)
        {
          RegexMatchResult keyFrameIdentifier;

          if (keyFrameElements.Count > 0)
          {
            keyFrameIdentifier = Expect(parser.Tokenizer.Match(identifierRegEx), "@keyframe block unknown identifier", parser);
          }
          else
          {
            keyFrameIdentifier = parser.Tokenizer.Match(identifierRegEx);
            if (!keyFrameIdentifier)
            {
              break;
            }
          }

          keyFrameElements.Add(new Element(null, keyFrameIdentifier));

          GatherComments(parser);

          if (!parser.Tokenizer.Match(","))
            break;

          GatherComments(parser);
        }

        if (keyFrameElements.Count == 0)
          break;

        var preComments = GatherAndPullComments(parser);

        var block = Expect(Block(parser), "Expected css block after key frame identifier", parser);

        block.PreComments = preComments;
        block.PostComments = GatherAndPullComments(parser);

        keyFrames.Add(NodeProvider.KeyFrame(keyFrameElements, block, parser.Tokenizer.GetNodeLocation()));
      }

      Expect(parser, '}', "Expected start, finish, % or '}}' but got {1}");

      return NodeProvider.Directive(name, identifier, keyFrames, parser.Tokenizer.GetNodeLocation(index));
    }

    public Value Font(CartoParser parser)
    {
      var value = new NodeList();
      var expression = new NodeList();
      Node e;

      var index = parser.Tokenizer.Location.Index;

      while (e = Shorthand(parser) || Entity(parser))
      {
        expression.Add(e);
      }
      value.Add(NodeProvider.Expression(expression, parser.Tokenizer.GetNodeLocation(index)));

      if (parser.Tokenizer.Match(','))
      {
        while (e = Expression(parser))
        {
          value.Add(e);
          if (!parser.Tokenizer.Match(','))
            break;
        }
      }
      return NodeProvider.Value(value, Important(parser), parser.Tokenizer.GetNodeLocation(index));
    }

    //
    // A Value is a comma-delimited list of Expressions
    //
    //     font-family: Baskerville, Georgia, serif;
    //
    // In a Rule, a Value represents everything after the `:`,
    // and before the `;`.
    //
    public Value Value(CartoParser parser)
    {
      var expressions = new NodeList();

      var index = parser.Tokenizer.Location.Index;

      Node e;
      while (e = Expression(parser))
      {
        expressions.Add(e);
        if (!parser.Tokenizer.Match(','))
          break;
      }

      GatherComments(parser);

      var important = Important(parser);

      if (expressions.Count > 0)
      {
        var value = NodeProvider.Value(expressions, important, parser.Tokenizer.GetNodeLocation(index));

        if (!string.IsNullOrEmpty(important))
        {
          value.PreImportantComments = PullComments();
        }

        return value;
      }

      return null;
    }

    public string Important(CartoParser parser)
    {
      var important = parser.Tokenizer.Match(@"!\s*important");

      return important == null ? "" : important.Value;
    }

    public Expression Sub(CartoParser parser)
    {
      if (!parser.Tokenizer.Match('('))
        return null;

      var memo = Remember(parser);

      var e = Expression(parser);
      if (e != null && parser.Tokenizer.Match(')'))
        return e;

      Recall(parser, memo);

      return null;
    }

    public Node Multiplication(CartoParser parser)
    {
      GatherComments(parser);

      var m = Operand(parser);
      if (!m)
        return null;

      Node operation = m;

      while (true)
      {
        GatherComments(parser); // after left operand

        var index = parser.Tokenizer.Location.Index;
        var op = parser.Tokenizer.Match(@"[\/*]");

        GatherComments(parser); // after operation

        Node a = null;
        if (op && (a = Operand(parser)))
          operation = NodeProvider.Operation(op.Value, operation, a, parser.Tokenizer.GetNodeLocation(index));
        else
          break;
      }
      return operation;
    }

    public Node Addition(CartoParser parser)
    {
      var m = Multiplication(parser);
      if (!m)
        return null;

      Operation operation = null;
      while (true)
      {
        GatherComments(parser);

        var index = parser.Tokenizer.Location.Index;
        var op = parser.Tokenizer.Match(@"[-+]\s+");
        if (!op && !char.IsWhiteSpace(parser.Tokenizer.GetPreviousCharIgnoringComments()))
          op = parser.Tokenizer.Match(@"[-+]");

        Node a = null;
        if (op && (a = Multiplication(parser)))
          operation = NodeProvider.Operation(op.Value, operation ?? m, a, parser.Tokenizer.GetNodeLocation(index));
        else
          break;
      }
      return operation ?? m;
    }

    //
    // An operand is anything that can be part of an operation,
    // such as a Color, or a Variable
    //
    public Node Operand(CartoParser parser)
    {
      return Sub(parser) || Entity(parser);
      /*CharMatchResult negate = null;

      if (parser.Tokenizer.CurrentChar == '-' && parser.Tokenizer.Peek(@"-[@\(]"))
      {
        negate = parser.Tokenizer.Match('-');
        GatherComments(parser);
      }

      var operand = Sub(parser) ??
                    Dimension(parser) ??
                    Color(parser) ??
                    (Node)Variable(parser);

      if (operand != null)
      {
        return negate ?
            NodeProvider.Operation("*", NodeProvider.Number("-1", "", negate.Location), operand, negate.Location) :
            operand;
      }

      if (parser.Tokenizer.CurrentChar == 'u' && parser.Tokenizer.Peek(@"url\("))
        return null;

      return Call(parser) || Keyword(parser);*/
    }

    //
    // Expressions either represent mathematical operations,
    // or white-space delimited Entities.
    //
    //     1px solid black
    //     @var * 2
    //
    public Expression Expression(CartoParser parser)
    {
      Node e;
      var entities = new NodeList();

      var index = parser.Tokenizer.Location.Index;

#if CSS3EXPERIMENTAL
            while (e = RepeatPattern(parser) || Addition(parser) || Entity(parser))
#else
      while (e = Addition(parser) || Entity(parser))
#endif
      {
        e.PostComments = PullComments();
        entities.Add(e);
      }

      if (entities.Count > 0)
        return NodeProvider.Expression(entities, parser.Tokenizer.GetNodeLocation(index));

      return null;
    }

#if CSS3EXPERIMENTAL
        /// <summary>
        ///  A repeat entity.. such as "(0.5in * *)[2]"
        /// </summary>
        public Node RepeatPattern(CartoCSSParser parser)
        {
            if (parser.Tokenizer.Peek(@"\([^;{}\)]+\)\[")) {
                var index = parser.Tokenizer.Location.Index;

                parser.Tokenizer.Match('(');
                var value = Expression(parser);
                Expect(parser, ')');
                Expect(parser, '[');
                var repeat = Expect(Entity(parser), "Expected repeat entity", parser);
                Expect(parser, ']');

                return NodeProvider.RepeatEntity(NodeProvider.Paren(value, index), repeat, index);
            }

            return null;
        }
#endif

    public Node Property(CartoParser parser)
    {
      var name = parser.Tokenizer.Match(@"^(([a-z][-a-z_0-9]*\/)?\*?-?[-a-z_0-9]+)\s*");// "\*?-?[-_a-zA-Z][-_a-z0-9A-Z]*"

      return name;
    }

    public string PropertyValue(CartoParser parser)
    {
      var name = parser.Tokenizer.Match(@"\*?-?[-_a-zA-Z][-_a-z0-9A-Z]*");

      if (name)
        return name.Value;

      return null;
    }

    public void Expect(CartoParser parser, char expectedString)
    {
      Expect(parser, expectedString, null);
    }

    public void Expect(CartoParser parser, char expectedString, string message)
    {
      if (parser.Tokenizer.Match(expectedString))
        return;

      message = message ?? "Expected '{0}' but found '{1}'";

      throw new ParsingException(string.Format(message, expectedString, parser.Tokenizer.NextChar), parser.Tokenizer.GetNodeLocation());
    }

    public T Expect<T>(T node, string message, CartoParser parser) where T : Node
    {
      if (node)
        return node;

      throw new ParsingException(message, parser.Tokenizer.GetNodeLocation());
    }


    internal class ParserLocation
    {
      public NodeList Comments { get; set; }
      public Location TokenizerLocation { get; set; }
    }

    public ParserLocation Remember(CartoParser parser)
    {
      return new ParserLocation() { Comments = CurrentComments, TokenizerLocation = parser.Tokenizer.Location };
    }

    public void Recall(CartoParser parser, ParserLocation location)
    {
      CurrentComments = location.Comments;
      parser.Tokenizer.Location = location.TokenizerLocation;
    }
  }
}