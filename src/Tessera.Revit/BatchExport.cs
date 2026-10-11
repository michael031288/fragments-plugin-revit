using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Tessera.Revit;

/// <summary>
/// Unattended folder conversion. When Revit starts with TESSERA_BATCH_JOB set to a job
/// file (written by tools\batch\Convert.ps1), every listed model is opened in the
/// background, exported with <see cref="TesseraRevitExporter"/> and closed without
/// saving. Without that variable this class does nothing.
/// </summary>
internal static class BatchExport
{
    public const string JobVariable = "TESSERA_BATCH_JOB";
    private static bool _started;
    private static BatchJob? _job;
    private static string _log = "";

    public static void Attach(UIControlledApplication application)
    {
        var jobPath = Environment.GetEnvironmentVariable(JobVariable);
        if (_started || string.IsNullOrEmpty(jobPath) || !File.Exists(jobPath))
        {
            return;
        }

        _started = true; // one run per Revit process, even if the add-in is registered twice
        try
        {
            _job = JsonSerializer.Deserialize<BatchJob>(File.ReadAllText(jobPath), JsonOptions)
                   ?? throw new InvalidDataException("Empty job file.");
            Directory.CreateDirectory(_job.OutDir);
            _log = Path.Combine(_job.OutDir, "_export_log.txt");
            Log("=== Tessera batch export started ===");
            application.DialogBoxShowing += OnDialog;
            application.ControlledApplication.FailuresProcessing += OnFailures;
            application.ControlledApplication.ApplicationInitialized += OnInitialized;
        }
        catch (Exception ex)
        {
            WriteSummary(Path.GetDirectoryName(jobPath)!, [new BatchResult { Status = "failed", Error = "Job: " + ex.Message }]);
        }
    }

    // Dismiss whatever Revit asks during the run (missing links, upgrade notices, ...).
    private static void OnDialog(object? sender, DialogBoxShowingEventArgs e)
    {
        Log("  Dialog dismissed: " + e.DialogId);
        e.OverrideResult(1);
    }

    // Warnings never block an unattended export.
    private static void OnFailures(object? sender, FailuresProcessingEventArgs e)
    {
        try
        {
            e.GetFailuresAccessor().DeleteAllWarnings();
            e.SetProcessingResult(FailureProcessingResult.Continue);
        }
        catch
        {
            // Leave it to Revit.
        }
    }

    private static void OnInitialized(object? sender, ApplicationInitializedEventArgs e)
    {
        var job = _job!;
        var results = new List<BatchResult>();
        try
        {
            var app = (RevitApplication)sender!;
            Log($"Revit {app.VersionNumber}, detail {job.Detail}, {(job.SharedCoordinates ? "shared" : "project")} coordinates");
            foreach (var model in job.Models)
            {
                var result = new BatchResult { Model = model };
                try
                {
                    var summary = ExportOne(app, model, job);
                    result.Status = "ok";
                    result.Tsra = summary.File;
                    result.Elements = summary.ElementCount;
                    result.Triangles = summary.TriangleCount;
                    result.Bytes = summary.FileBytes;
                }
                catch (Exception ex)
                {
                    result.Status = "failed";
                    result.Error = ex.Message;
                    Log("  FAILED: " + ex);
                }

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            results.Add(new BatchResult { Status = "failed", Error = ex.Message });
            Log("FATAL: " + ex);
        }

        Log("=== Done ===");
        WriteSummary(job.OutDir, results); // the launcher closes Revit once this appears
    }

    private static TesseraExportSummary ExportOne(RevitApplication app, string path, BatchJob job)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        Log("Opening " + Path.GetFileName(path));

        var options = new OpenOptions { Audit = false };
        if (BasicFileInfo.Extract(path).IsWorkshared)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }

        var document = app.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options);
        try
        {
            var view = FindView(document, job.ViewName);
            Log("  View: " + view.Name);
            var final = Path.Combine(job.OutDir, name + ".tsra");
            var temp = final + ".tmp";
            var summary = TesseraRevitExporter.Export(document, view, temp, new TesseraExportSettings
            {
                LevelOfDetail = job.Detail switch
                {
                    "coarse" => 2,
                    "fine" => 15,
                    "view" => -1,
                    _ => 8
                },
                UseSharedCoordinates = job.SharedCoordinates
            });
            File.Move(temp, final, overwrite: true); // an existing .tsra is only replaced by a finished one
            Log($"  OK -> {final} ({summary.ElementCount} elements, {summary.TriangleCount} triangles, {summary.FileBytes / 1048576.0:F1} MB)");
            return new TesseraExportSummary
            {
                ElementCount = summary.ElementCount,
                TriangleCount = summary.TriangleCount,
                FileBytes = summary.FileBytes,
                File = final
            };
        }
        finally
        {
            document.Close(false); // never save the source model
        }
    }

    /// <summary>The named 3D view, else "{3D}", else any 3D view, else a new isometric view (never saved).</summary>
    private static View3D FindView(Document document, string? viewName)
    {
        var views = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(view => !view.IsTemplate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var named = views.FirstOrDefault(view => view.Name == viewName);
            if (named != null)
            {
                return named;
            }

            Log($"  WARNING: no 3D view named '{viewName}', using the default 3D view");
        }

        var chosen = views.FirstOrDefault(view => view.Name == "{3D}") ?? views.FirstOrDefault();
        if (chosen != null)
        {
            return chosen;
        }

        var family = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(type => type.ViewFamily == ViewFamily.ThreeDimensional);
        using var transaction = new Transaction(document, "Tessera batch 3D view");
        transaction.Start();
        var created = View3D.CreateIsometric(document, family.Id);
        transaction.Commit();
        return created;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(_log, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best effort.
        }
    }

    private static void WriteSummary(string directory, List<BatchResult> results)
    {
        File.WriteAllText(Path.Combine(directory, "_last_run.json"), JsonSerializer.Serialize(results, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private sealed class BatchJob
    {
        public string OutDir { get; set; } = "";
        public List<string> Models { get; set; } = [];
        public string Detail { get; set; } = "medium";
        public bool SharedCoordinates { get; set; }
        public string? ViewName { get; set; }
    }

    private sealed class BatchResult
    {
        public string Model { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Tsra { get; set; }
        public string? Error { get; set; }
        public int Elements { get; set; }
        public int Triangles { get; set; }
        public long Bytes { get; set; }
    }
}
