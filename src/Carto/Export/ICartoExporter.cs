using System;

using MapSurfer.Logging;

namespace MapSurfer.Styling.Formats.CartoCSS.Export
{
	/// <summary>
	/// Description of ICartoExporter.
	/// </summary>
	internal interface ICartoExporter
	{
		void Export(string projectPath, string outPath, Logger logger);
	}
}
