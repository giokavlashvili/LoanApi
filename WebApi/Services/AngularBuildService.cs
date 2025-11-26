using System.Diagnostics;

namespace WebApi.Services;

/// <summary>
/// Service responsible for building Angular SPA automatically when running in containers.
/// </summary>
public static class AngularBuildService
{
    /// <summary>
    /// Checks if the application is running in a container.
    /// </summary>
    public static bool IsRunningInContainer()
    {
        // Check for DOTNET_RUNNING_IN_CONTAINER environment variable (set by ASP.NET Core)
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return true;
        }

        // Additional container detection for Linux environments
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            // Check for Docker-specific files
            if (File.Exists("/.dockerenv"))
            {
                return true;
            }

            // Check cgroup for Docker
            if (File.Exists("/proc/self/cgroup"))
            {
                try
                {
                    var cgroupContent = File.ReadAllText("/proc/self/cgroup");
                    if (cgroupContent.Contains("docker", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore errors reading cgroup
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Builds Angular SPA if it hasn't been built yet.
    /// </summary>
    /// <param name="contentRootPath">The content root path of the application.</param>
    /// <param name="logger">Logger instance for logging build progress.</param>
    /// <returns>True if build was successful or already exists, false otherwise.</returns>
    public static bool BuildIfNeeded(string contentRootPath, ILogger logger)
    {
        var wwwrootPath = Path.Combine(contentRootPath, "wwwroot");
        var indexHtmlPath = Path.Combine(wwwrootPath, "index.html");
        var clientAppPath = Path.Combine(contentRootPath, "ClientApp");
        var packageJsonPath = Path.Combine(clientAppPath, "package.json");

        // Check if Angular is already built
        //if (File.Exists(indexHtmlPath))
        //{
        //    logger.LogDebug("Angular SPA already built. Skipping build.");
        //    return true;
        //}

        // Check if ClientApp directory exists
        if (!Directory.Exists(clientAppPath) || !File.Exists(packageJsonPath))
        {
            logger.LogWarning("ClientApp directory or package.json not found. Skipping Angular build.");
            return false;
        }

        logger.LogInformation("Building SPA...");

        try
        {
            // Step 1: Run npm install
            if (!RunNpmCommand(clientAppPath, "install", logger))
            {
                logger.LogWarning("npm install completed with warnings. Continuing with build...");
            }

            // Step 2: Run npm build
            if (!RunNpmCommand(clientAppPath, "run build", logger))
            {
                logger.LogError("npm build failed. SPA may not be available.");
                return false;
            }

            // Step 3: Copy built files to wwwroot
            if (!CopyBuildOutputToWwwroot(clientAppPath, wwwrootPath, logger))
            {
                logger.LogError("Failed to copy Angular build output to wwwroot.");
                return false;
            }

            logger.LogInformation("Angular build complete!");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build Angular automatically. Please build manually.");
            return false;
        }
    }

    /// <summary>
    /// Runs an npm command in the specified directory.
    /// </summary>
    private static bool RunNpmCommand(string workingDirectory, string arguments, ILogger logger)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                logger.LogError("Failed to start npm process.");
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running npm command: {Arguments}", arguments);
            return false;
        }
    }

    /// <summary>
    /// Copies the Angular build output to wwwroot directory.
    /// </summary>
    private static bool CopyBuildOutputToWwwroot(string clientAppPath, string wwwrootPath, ILogger logger)
    {
        try
        {
            // Try dist/browser first (newer Angular build system)
            var distBrowserPath = Path.Combine(clientAppPath, "dist", "browser");
            var distPath = Path.Combine(clientAppPath, "dist");

            string? sourcePath = null;
            if (Directory.Exists(distBrowserPath))
            {
                sourcePath = distBrowserPath;
                logger.LogDebug("Found Angular build output in dist/browser");
            }
            else if (Directory.Exists(distPath))
            {
                sourcePath = distPath;
                logger.LogDebug("Found Angular build output in dist");
            }

            if (sourcePath == null || !Directory.Exists(sourcePath))
            {
                logger.LogWarning("Angular build output not found. Expected dist/ or dist/browser/ folder.");
                return false;
            }

            // Create wwwroot directory if it doesn't exist
            Directory.CreateDirectory(wwwrootPath);

            // Copy all files from dist to wwwroot
            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourcePath, file);
                var destPath = Path.Combine(wwwrootPath, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, destPath, overwrite: true);
            }

            logger.LogInformation("Copied {FileCount} files from {SourcePath} to {DestPath}", 
                files.Length, sourcePath, wwwrootPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error copying Angular build output to wwwroot");
            return false;
        }
    }
}

