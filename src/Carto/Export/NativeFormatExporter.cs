using System;
using System.IO;

using MapSurfer.Logging;

namespace MapSurfer.Styling.Formats.CartoCSS.Export
{
	/// <summary>
	/// Description of NativeFormatExporter.
	/// </summary>
	internal class NativeFormatExporter : ICartoExporter
	{
		public void Export(string projectPath, string outPath, Logger logger)
		{
			CartoProject cartoProject = CartoProject.FromFile(File.ReadAllText(projectPath), Path.GetExtension(projectPath));
			Map map = CartoProcessor.GetMap(cartoProject, Path.GetDirectoryName(projectPath), new NullProgressIndicator(), logger,  1);
			
			string dir = Path.GetDirectoryName(outPath);
			if(!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			
			if (Path.GetExtension(outPath) !=  ".msnm")
				outPath = Path.ChangeExtension(outPath, ".msnm");
			
			map.Save(outPath);
		}
	}
}
