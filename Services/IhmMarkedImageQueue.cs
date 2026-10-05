using Microsoft.EntityFrameworkCore;
using Survey_Certification_App.Data;
using Survey_Certification_App.Models.Survey;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Survey_Certification_App.Services;

public sealed record IhmMarkedImageQueueItem(
    int JobId,
    IhmMarkedImageSectionType SectionType,
    bool Force
);

public interface IIhmMarkedImageQueue
{
    void Queue(int jobId, IhmMarkedImageSectionType sectionType, bool force = false);
}

public class IhmMarkedImageQueue : BackgroundService, IIhmMarkedImageQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IhmMarkedImageQueue> _logger;
    private readonly ConcurrentQueue<IhmMarkedImageQueueItem> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _running = new();
    private readonly ConcurrentDictionary<string, IhmMarkedImageQueueItem> _rerunAfterRunning = new();
    private readonly SemaphoreSlim _signal = new(0);

    public IhmMarkedImageQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<IhmMarkedImageQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Queue(int jobId, IhmMarkedImageSectionType sectionType, bool force = false)
    {
        var key = $"{jobId}:{sectionType}";

        if (_running.ContainsKey(key))
        {
            _rerunAfterRunning[key] = new IhmMarkedImageQueueItem(jobId, sectionType, true);

            _logger.LogInformation(
                "Marked image generation already running. Queued rerun. jobId={JobId} section={Section}",
                jobId,
                sectionType
            );
            return;
        }

        _running.TryAdd(key, 0);
        _queue.Enqueue(new IhmMarkedImageQueueItem(jobId, sectionType, force));
        _signal.Release();

        _logger.LogInformation(
            "Queued marked image generation. jobId={JobId} section={Section} force={Force}",
            jobId,
            sectionType,
            force
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (!_queue.TryDequeue(out var item))
                continue;

            var key = $"{item.JobId}:{item.SectionType}";

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

                if (item.SectionType == IhmMarkedImageSectionType.Appendix4)
                    await GenerateAppendix4Async(db, env, item, stoppingToken);

                if (item.SectionType == IhmMarkedImageSectionType.Hazardous)
                    await GenerateHazardousAsync(db, env, item, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Marked image generation failed. jobId={JobId} section={Section}",
                    item.JobId,
                    item.SectionType
                );
            }
            finally
            {
                _running.TryRemove(key, out _);

                if (_rerunAfterRunning.TryRemove(key, out var rerunItem))
                {
                    _running.TryAdd(key, 0);
                    _queue.Enqueue(rerunItem);
                    _signal.Release();
                }
            }
        }
    }

    private static void DeleteMatchingFilesExcept(
        string folder,
        string searchPattern,
        IReadOnlySet<string> keepFileNames)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var filePath in Directory.GetFiles(folder, searchPattern))
        {
            if (keepFileNames.Contains(Path.GetFileName(filePath)))
                continue;

            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Best-effort cleanup only. Generation success should not fail because a stale file is locked.
            }
        }
    }

    private static HashSet<string> ReadHazardousManifestImageFileNames(string manifestPath)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFileName(manifestPath),
            "hazardous-i2-input.json"
        };

        if (!File.Exists(manifestPath))
            return keep;

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        if (!document.RootElement.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array)
            return keep;

        foreach (var image in images.EnumerateArray())
        {
            var imagePath = image.GetString();
            if (!string.IsNullOrWhiteSpace(imagePath))
                keep.Add(Path.GetFileName(imagePath));
        }

        return keep;
    }

    private async Task GenerateAppendix4Async(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IhmMarkedImageQueueItem item,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var gaLayout = await db.IhmSamplingPhotos
    .Where(x => x.SurveyJobId == item.JobId &&
                x.IhmAreaId == null &&
                x.IhmCheckpointId == null &&
                x.Caption == "Full GA layout")
    .OrderByDescending(x => x.UploadedAt)
    .FirstOrDefaultAsync(cancellationToken);

        if (gaLayout == null)
        {
            gaLayout = await db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == item.JobId &&
                            x.IhmAreaId != null &&
                            x.IhmCheckpointId == null)
                .OrderBy(x => x.IhmAreaId)
                .ThenByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (gaLayout == null)
        {
            gaLayout = await db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == item.JobId &&
                            x.IhmAreaId == null &&
                            x.IhmCheckpointId == null &&
                            x.Caption == "Machinery layout" &&
                            x.AssignedAreaIds != null &&
                            x.AssignedAreaIds != "")
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (gaLayout == null)
            {
                _logger.LogInformation("No common or area layout uploaded. Skipping Appendix 4 marked image generation. jobId={JobId}", item.JobId);
                return;
            }

            var sourcePhysicalPath = ResolveWebPath(env, gaLayout.FilePath);
            var sourceModifiedUtc = File.Exists(sourcePhysicalPath)
                ? File.GetLastWriteTimeUtc(sourcePhysicalPath)
                : (DateTime?)null;

            var markerPayload = await BuildAppendix4MarkerPayload(db, item.JobId, cancellationToken);
            var markerHash = ComputeHash(markerPayload);

            var metadata = await db.IhmMarkedImageMetadata.FirstOrDefaultAsync(x =>
                x.SurveyJobId == item.JobId &&
                x.SectionType == IhmMarkedImageSectionType.Appendix4 &&
                x.SourceImagePath == gaLayout.FilePath,
                cancellationToken);

            if (metadata != null &&
                !item.Force &&
                metadata.Status == IhmMarkedImageStatus.Completed &&
                metadata.SourceImageLastModifiedUtc == sourceModifiedUtc &&
                metadata.MarkerDataHash == markerHash &&
                !string.IsNullOrWhiteSpace(metadata.GeneratedImagePath) &&
                File.Exists(ResolveWebPath(env, metadata.GeneratedImagePath)))
            {
                _logger.LogInformation(
                    "Marked image already current. jobId={JobId} section=Appendix4 path={Path}",
                    item.JobId,
                    metadata.GeneratedImagePath
                );
                return;
            }

            metadata ??= new IhmMarkedImageMetadata
            {
                SurveyJobId = item.JobId,
                SectionType = IhmMarkedImageSectionType.Appendix4,
                SourceImagePath = gaLayout.FilePath,
                CreatedAtUtc = DateTime.UtcNow
            };

            metadata.Status = IhmMarkedImageStatus.Processing;
            metadata.SourceImageLastModifiedUtc = sourceModifiedUtc;
            metadata.MarkerDataHash = markerHash;
            metadata.LastQueuedAtUtc = DateTime.UtcNow;
            metadata.UpdatedAtUtc = DateTime.UtcNow;
            metadata.ErrorMessage = null;

            if (metadata.Id == 0)
                db.IhmMarkedImageMetadata.Add(metadata);

            await db.SaveChangesAsync(cancellationToken);

            try
            {
                var reportFolder = Path.Combine(
                    env.WebRootPath,
                    "reports",
                    "ihm",
                    item.JobId.ToString()
                );

                var generatedFolder = Path.Combine(reportFolder, "generated-images");
                Directory.CreateDirectory(generatedFolder);

                var inputJsonPath = Path.Combine(generatedFolder, "appendix4-ga-map-input.json");
                var placementJsonPath = Path.Combine(generatedFolder, "appendix4-ga-map-placements.json");
                var outputImagePhysicalPath = Path.Combine(
                    generatedFolder,
                    $"appendix4-ga-marker-map-{markerHash[..16]}.png"
                );

                await File.WriteAllTextAsync(inputJsonPath, markerPayload, cancellationToken);

                var scriptPath = Path.Combine(
                    env.ContentRootPath,
                    "PythonServices",
                    "generate_ga_marker_map.py"
                );

                var pythonExe = File.Exists(Path.Combine(env.ContentRootPath, ".venv", "Scripts", "python.exe"))
                    ? Path.Combine(env.ContentRootPath, ".venv", "Scripts", "python.exe")
                    : File.Exists(Path.Combine(env.ContentRootPath, "venv", "Scripts", "python.exe"))
                        ? Path.Combine(env.ContentRootPath, "venv", "Scripts", "python.exe")
                        : "python";

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{inputJsonPath}\" \"{outputImagePhysicalPath}\" \"{placementJsonPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var userPythonPackages = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Python", "Python314", "site-packages");
                if (Directory.Exists(userPythonPackages))
                    startInfo.Environment["PYTHONPATH"] = userPythonPackages;

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to start GA map generator.");

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);

                metadata.GeneratedImagePath =
                    $"/reports/ihm/{item.JobId}/generated-images/{Path.GetFileName(outputImagePhysicalPath)}";
                metadata.Status = IhmMarkedImageStatus.Completed;
                metadata.GeneratedAtUtc = DateTime.UtcNow;
                metadata.UpdatedAtUtc = DateTime.UtcNow;
                metadata.ErrorMessage = null;

                await db.SaveChangesAsync(cancellationToken);

                DeleteMatchingFilesExcept(
                    generatedFolder,
                    "appendix4-ga-marker-map-*.png",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                    Path.GetFileName(outputImagePhysicalPath)
                    }
                );

                DeleteMatchingFilesExcept(
                    generatedFolder,
                    "appendix4-*.tmp",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                );

                _logger.LogInformation(
                    "Completed marked image generation. jobId={JobId} section=Appendix4 elapsed_ms={ElapsedMs} path={Path}",
                    item.JobId,
                    sw.ElapsedMilliseconds,
                    metadata.GeneratedImagePath
                );
            }
            catch (Exception ex)
            {
                metadata.Status = IhmMarkedImageStatus.Failed;
                metadata.RetryCount += 1;
                metadata.ErrorMessage = TruncateGeneratorLog(ex.ToString(), 3900);
                metadata.UpdatedAtUtc = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Failed marked image generation. jobId={JobId} section=Appendix4",
                    item.JobId
                );
            }
        }
    }

    private static async Task<string> BuildAppendix4MarkerPayload(
        ApplicationDbContext db,
        int jobId,
        CancellationToken cancellationToken)
    {
        var gaLayout = await db.IhmSamplingPhotos
    .Where(x => x.SurveyJobId == jobId &&
                x.IhmAreaId == null &&
                x.IhmCheckpointId == null &&
                x.Caption == "Full GA layout")
    .OrderByDescending(x => x.UploadedAt)
    .Select(x => x.FilePath)
    .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(gaLayout))
        {
            gaLayout = await db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId != null &&
                            x.IhmCheckpointId == null)
                .OrderBy(x => x.IhmAreaId)
                .ThenByDescending(x => x.UploadedAt)
                .Select(x => x.FilePath)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(gaLayout))
        {
            gaLayout = await db.IhmSamplingPhotos
                .Where(x => x.SurveyJobId == jobId &&
                            x.IhmAreaId == null &&
                            x.IhmCheckpointId == null &&
                            x.Caption == "Machinery layout" &&
                            x.AssignedAreaIds != null &&
                            x.AssignedAreaIds != "")
                .OrderBy(x => x.Id)
                .Select(x => x.FilePath)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var machineryLayouts = await db.IhmSamplingPhotos
            .Where(x => x.SurveyJobId == jobId &&
                        x.IhmAreaId == null &&
                        x.IhmCheckpointId == null &&
                        x.Caption == "Machinery layout")
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new { x.Id, x.FilePath, x.OriginalFileName, x.AssignedAreaIds })
            .ToListAsync(cancellationToken);

        var areaLayouts = await db.IhmSamplingPhotos
            .Where(x => x.SurveyJobId == jobId &&
                        x.IhmAreaId != null &&
                        x.IhmCheckpointId == null)
            .OrderBy(x => x.IhmAreaId)
            .Select(x => new
            {
                x.Id,
                x.IhmAreaId,
                AreaName = x.IhmArea != null ? x.IhmArea.AreaName : null,
                x.OriginalFileName,
                x.FilePath
            })
            .ToListAsync(cancellationToken);

        var markers = await db.IhmImageMarkers
            .Where(x => x.SurveyJobId == jobId && x.IhmAreaId != null)
            .OrderBy(x => x.IhmAreaId)
            .ThenBy(x => x.IhmCheckpointId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.IhmAreaId,
                x.IhmCheckpointId,
                x.ImagePath,
                x.MarkerLabel,
                x.XPercent,
                x.YPercent,
                x.Notes,
                x.Source,
                x.SuppressRender,
                x.IsManualForExistingImage
            })
            .ToListAsync(cancellationToken);

        var placements = await db.IhmAreaLayoutPlacements
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.IhmAreaId)
            .Select(x => new
            {
                x.IhmAreaId,
                x.XPercent,
                x.YPercent,
                x.WidthPercent,
                x.HeightPercent,
                x.MatchConfidence,
                x.IsAutoMatched,
                x.IsManuallyAdjusted
            })
            .ToListAsync(cancellationToken);

        var payload = new
        {
            GaLayout = gaLayout,
            MachineryLayouts = machineryLayouts,
            MachineryLayout = machineryLayouts.FirstOrDefault()?.FilePath,
            AreaLayouts = areaLayouts,
            Markers = markers,
            Placements = placements
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ResolveWebPath(IWebHostEnvironment env, string webPath)
    {
        return Path.Combine(
            env.WebRootPath,
            webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)
        );
    }

    private async Task GenerateHazardousAsync(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IhmMarkedImageQueueItem item,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var markerPayload = await BuildHazardousMarkerPayload(db, item.JobId, cancellationToken);
        var markerHash = ComputeHash(markerPayload);
        const string sourcePath = "I-2 hazardous marked layouts";

        var metadata = await db.IhmMarkedImageMetadata.FirstOrDefaultAsync(x =>
            x.SurveyJobId == item.JobId &&
            x.SectionType == IhmMarkedImageSectionType.Hazardous &&
            x.SourceImagePath == sourcePath,
            cancellationToken);

        if (metadata != null &&
            !item.Force &&
            metadata.Status == IhmMarkedImageStatus.Completed &&
            metadata.MarkerDataHash == markerHash &&
            !string.IsNullOrWhiteSpace(metadata.GeneratedImagePath) &&
            File.Exists(ResolveWebPath(env, metadata.GeneratedImagePath)))
        {
            _logger.LogInformation(
                "Marked image already current. jobId={JobId} section=Hazardous path={Path}",
                item.JobId,
                metadata.GeneratedImagePath
            );
            return;
        }

        metadata ??= new IhmMarkedImageMetadata
        {
            SurveyJobId = item.JobId,
            SectionType = IhmMarkedImageSectionType.Hazardous,
            SourceImagePath = sourcePath,
            CreatedAtUtc = DateTime.UtcNow
        };

        metadata.Status = IhmMarkedImageStatus.Processing;
        metadata.MarkerDataHash = markerHash;
        metadata.SourceImageLastModifiedUtc = null;
        metadata.LastQueuedAtUtc = DateTime.UtcNow;
        metadata.UpdatedAtUtc = DateTime.UtcNow;
        metadata.ErrorMessage = null;

        if (metadata.Id == 0)
            db.IhmMarkedImageMetadata.Add(metadata);

        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var reportFolder = Path.Combine(
                env.WebRootPath,
                "reports",
                "ihm",
                item.JobId.ToString()
            );
            var generatedFolder = Path.Combine(reportFolder, "generated-images", "hazardous");
            Directory.CreateDirectory(generatedFolder);

            var inputJsonPath = Path.Combine(generatedFolder, "hazardous-i2-input.json");
            var manifestPhysicalPath = Path.Combine(generatedFolder, "hazardous-i2-layout-manifest.json");
            await File.WriteAllTextAsync(inputJsonPath, markerPayload, cancellationToken);

            var scriptPath = Path.Combine(
                env.ContentRootPath,
                "PythonServices",
                "generate_hazard_marked_images.py"
            );

            var pythonExe = File.Exists(Path.Combine(env.ContentRootPath, ".venv", "Scripts", "python.exe"))
                ? Path.Combine(env.ContentRootPath, ".venv", "Scripts", "python.exe")
                : File.Exists(Path.Combine(env.ContentRootPath, "venv", "Scripts", "python.exe"))
                    ? Path.Combine(env.ContentRootPath, "venv", "Scripts", "python.exe")
                    : "python";

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" \"{inputJsonPath}\" \"{generatedFolder}\" \"{manifestPhysicalPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var userPythonPackages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Python", "Python314", "site-packages");
            if (Directory.Exists(userPythonPackages))
                startInfo.Environment["PYTHONPATH"] = userPythonPackages;

            _logger.LogInformation(
                "Starting hazardous Python generator. jobId={JobId} executable={Executable} script={Script} input={Input} output={Output} pythonPath={PythonPath}",
                item.JobId,
                startInfo.FileName,
                scriptPath,
                inputJsonPath,
                manifestPhysicalPath,
                startInfo.Environment.TryGetValue("PYTHONPATH", out var configuredPythonPath)
                    ? configuredPythonPath
                    : "<none>");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start hazardous I-2 marked image generator.");

            _logger.LogInformation(
                "Hazardous Python process started. jobId={JobId} pid={ProcessId}",
                item.JobId,
                process.Id);

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            _logger.LogInformation(
                "Hazardous Python process exited. jobId={JobId} pid={ProcessId} exitCode={ExitCode} stdout={Stdout} stderr={Stderr}",
                item.JobId,
                process.Id,
                process.ExitCode,
                TruncateGeneratorLog(output),
                TruncateGeneratorLog(error));

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);

            metadata.GeneratedImagePath =
                $"/reports/ihm/{item.JobId}/generated-images/hazardous/{Path.GetFileName(manifestPhysicalPath)}";
            metadata.Status = IhmMarkedImageStatus.Completed;
            metadata.GeneratedAtUtc = DateTime.UtcNow;
            metadata.UpdatedAtUtc = DateTime.UtcNow;
            metadata.ErrorMessage = null;

            await db.SaveChangesAsync(cancellationToken);

            var keepHazardousFiles = ReadHazardousManifestImageFileNames(manifestPhysicalPath);
            DeleteMatchingFilesExcept(generatedFolder, "hazardous-i2-layout-*.png", keepHazardousFiles);
            DeleteMatchingFilesExcept(generatedFolder, "hazardous-i2-layout-*.tmp", keepHazardousFiles);

            _logger.LogInformation(
                "Completed marked image generation. jobId={JobId} section=Hazardous elapsed_ms={ElapsedMs} path={Path}",
                item.JobId,
                sw.ElapsedMilliseconds,
                metadata.GeneratedImagePath
            );
        }
        catch (Exception ex)
        {
            metadata.Status = IhmMarkedImageStatus.Failed;
            metadata.RetryCount += 1;
            metadata.ErrorMessage = ex.Message;
            metadata.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                ex,
                "Failed marked image generation. jobId={JobId} section=Hazardous",
                item.JobId
            );
        }
    }

    private static string TruncateGeneratorLog(string? value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<empty>";

        var normalized = value.Replace(Environment.NewLine, " ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + " ...[truncated]";
    }

    private static async Task<string> BuildHazardousMarkerPayload(
        ApplicationDbContext db,
        int jobId,
        CancellationToken cancellationToken)
    {
        var job = await db.SurveyJobs
            .Where(x => x.Id == jobId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);
        var gaLayout = await db.IhmSamplingPhotos
    .Where(x => x.SurveyJobId == jobId &&
                x.IhmAreaId == null &&
                x.IhmCheckpointId == null &&
                x.Caption == "Full GA layout")
    .OrderByDescending(x => x.UploadedAt)
    .Select(x => x.FilePath)
    .FirstOrDefaultAsync(cancellationToken);

        var machineryLayouts = await db.IhmSamplingPhotos
            .Where(x => x.SurveyJobId == jobId &&
                        x.IhmAreaId == null &&
                        x.IhmCheckpointId == null &&
                        x.Caption == "Machinery layout")
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new { x.Id, x.FilePath, x.OriginalFileName, x.AssignedAreaIds })
            .ToListAsync(cancellationToken);

        var activeAreaAssignedLayout = await db.IhmSamplingPhotos
            .Where(x => x.SurveyJobId == jobId &&
                        x.IhmCheckpointId == null &&
                        (x.IhmAreaId != null ||
                         (x.Caption == "Machinery layout" &&
                          x.AssignedAreaIds != null &&
                          x.AssignedAreaIds != "")))
            .OrderByDescending(x => x.UploadedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.FilePath)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeAreaAssignedLayout))
            gaLayout = activeAreaAssignedLayout;
        var equipmentAndMachinery = await db.IhmCheckpoints
            .Where(x => x.SurveyJobId == jobId && x.CheckResult == "Yes")
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.IhmAreaId,
                x.Sample_No,
                x.EquipmentName,
                x.Location,
                MaterialClassification = x.HazardousMaterial,
                PartsWereUsed = x.Component,
                UnitKg = x.QuantityUnitKg,
                x.QuantityNo,
                TotalKg = x.QuantityTotalKg,
                x.Remarks
            })
            .ToListAsync(cancellationToken);
                var areaLayouts = await db.IhmSamplingPhotos
            .Where(x => x.SurveyJobId == jobId &&
                        x.IhmAreaId != null &&
                        x.IhmCheckpointId == null)
            .OrderBy(x => x.IhmAreaId)
            .ThenByDescending(x => x.UploadedAt)
            .Select(x => new
            {
                x.Id,
                x.IhmAreaId,
                AreaName = x.IhmArea != null ? x.IhmArea.AreaName : null,
                x.OriginalFileName,
                x.FilePath
            })
            .ToListAsync(cancellationToken);
        var markers = await db.IhmImageMarkers
            .Where(x => x.SurveyJobId == jobId)
            .OrderBy(x => x.IhmAreaId)
            .ThenBy(x => x.IhmCheckpointId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.IhmAreaId,
                x.IhmCheckpointId,
                x.ImagePath,
                x.MarkerLabel,
                x.XPercent,
                x.YPercent,
                x.Notes,
                x.Source,
                x.SuppressRender,
                x.IsManualForExistingImage
            })
            .ToListAsync(cancellationToken);

        var payload = new
        {
            Job = job,
            GaLayout = gaLayout,
            MachineryLayouts = machineryLayouts,
            MachineryLayout = machineryLayouts.FirstOrDefault()?.FilePath,
            EquipmentAndMachinery = equipmentAndMachinery,
            AreaLayouts = areaLayouts,
            Markers = markers
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
