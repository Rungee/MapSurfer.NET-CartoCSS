using System;
using System.Diagnostics;
using System.IO;

using MapSurfer;
using MapSurfer.Logging;

namespace MapSurfer.Styling.Formats.CartoCSS.Export
{
	/// <summary>
	/// Description of MagnacartoExporter.
	/// </summary>
	internal class MagnacartoExporter : ICartoExporter
	{
		private string _format;
		private static string _exePath;
		
		static MagnacartoExporter()
		{
			_exePath = Path.Combine(Path.Combine(Path.GetDirectoryName(MapSurfer.Utilities.MSNEnvironment.GetAssemblyLocation(typeof(MagnacartoExporter).Assembly)), EnvironmentEx.GetNativePath()), "magnacarto.exe");
		}

    public static bool IsReady()
    {
      return File.Exists(_exePath);
    }
		
		public MagnacartoExporter(string format)
		{
			_format = format != null ? format.ToLower(): "mapnik2";
		}
		
		public void Export(string projectPath, string outPath, Logger logger)
		{
			if (string.IsNullOrEmpty(Path.GetExtension(outPath)))
		  {
				string ext = null;
				switch(_format)
				{
					case "mapnik2":
					case "mapnik3":
						ext = ".xml";
						break;
					case "mapserver":
						ext = ".map";
						break;
				}
				
				if (!string.IsNullOrEmpty(ext))
					outPath = Path.ChangeExtension(outPath, ext);
			}
			
			ProcessStartInfo startInfo = new ProcessStartInfo(_exePath);
			startInfo.Arguments = string.Format("-builder {0} -mml {1} -out {2}", _format, projectPath, outPath);
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.WindowStyle = ProcessWindowStyle.Hidden;
      startInfo.RedirectStandardOutput = true;
      startInfo.RedirectStandardError = true;
      //   Process.Start(startInfo);

      Process p = new Process();
      p.StartInfo = startInfo;
      p.StartInfo.RedirectStandardOutput = true;
      p.Start();

      string err = p.StandardError.ReadToEnd();

      if (!string.IsNullOrEmpty(err))
      {
        LogFactory.WriteLogEntry(logger, new Exception(err));
        throw new Exception(err);
      }
		}
	}
}
