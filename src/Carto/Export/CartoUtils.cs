using System;
using System.Collections.Generic;
using System.Windows.Forms;

using MapSurfer.Data;
using MapSurfer.Geometries;
using MapSurfer.Labeling;
using MapSurfer.Layers;
using MapSurfer.Styling.Formats.CartoCSS.Translators;
using MapSurfer.Utilities;
using MapSurfer.Logging;
using MapSurfer.Configuration;

namespace MapSurfer.Styling.Formats.CartoCSS.Export
{
  /// <summary>
  /// Description of CartoUtils.
  /// </summary>
  public static class CartoUtils
  {
    public static void Export(Func<string> getProjectPath, IWin32Window window, Logger logger)
    {
      List<string> formats = new List<string>();
      formats.Add("MapSurfer.NET");
      if (MagnacartoExporter.IsReady())
        formats.AddRange(new string[] { "Mapnik2", "Mapnik3", "MapServer" });

      using (CartoExportFileDialog dlg = new CartoExportFileDialog(formats.ToArray()))
      {
        if (dlg.ShowDialog(window) == System.Windows.Forms.DialogResult.OK)
        {
          ICartoExporter exporter = null;

          switch (dlg.Format)
          {
            case "MapSurfer.NET":
              exporter = new NativeFormatExporter();
              break;
            case "Mapnik2":
            case "Mapnik3":
            case "MapServer":
              exporter = new MagnacartoExporter(dlg.Format);
              break;
          }

          exporter.Export(getProjectPath(), dlg.FileName, logger);
        }
      }
    }
  }
}
