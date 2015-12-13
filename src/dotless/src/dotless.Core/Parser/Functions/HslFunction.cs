namespace dotless.Core.Parser.Functions
{
    using Infrastructure;
    using Infrastructure.Nodes;
    using Tree;
    using Utils;

    public class HslFunction : HslaFunction
    {
        protected override Node Evaluate(Env env)
        {
            Guard.ExpectNumArguments(3, Arguments.Count, this, Location);

            Arguments.Add(new Number(1d, ""));

            return base.Evaluate(env);
        }
    }
}