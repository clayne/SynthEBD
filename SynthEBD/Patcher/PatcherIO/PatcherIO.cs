using Microsoft.Extensions.Logging;
using Mutagen.Bethesda.Skyrim;
using System.IO;

namespace SynthEBD;

class PatcherIO
{
    public enum PathType
    {
        File,
        Directory
    }
    public static dynamic CreateDirectoryIfNeeded(string path, PathType type)
    {
        if (type == PathType.File)
        {
            FileInfo file = new FileInfo(path);
            file.Directory.Create(); // If the directory already exists, this method does nothing.
            return file;
        }
        else
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            directory.Create();
            return directory;
        }
    }

    public static async Task WriteTextFile(string path, string contents)
    {
        var file = CreateDirectoryIfNeeded(path, PathType.File);

        try
        {
            await File.WriteAllTextAsync(file.FullName, contents);
        }
        catch
        {
            Logger.LogError("Could not create file at " + path);
        }
    }
    public static async Task WriteTextFile(string path, List<string> contents)
    {
        await WriteTextFile(path, string.Join(Environment.NewLine, contents));
    }
    public static void WritePatch(string patchOutputPath, SkyrimMod outputMod)
    {
        string errStr = "";
        if (File.Exists(patchOutputPath))
        {
            try
            {
                File.Delete(patchOutputPath);
            }
            catch (Exception e)
            {
                errStr = ExceptionLogger.GetExceptionStack(e, errStr);
                Logger.LogMessage("Failed to delete previous version of patch. Error: " + Environment.NewLine + errStr);
                Logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error);
                return;
            }
        }

        try
        {
            var writeParams = new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryWriteParameters()
            {
                MastersListOrdering = new Mutagen.Bethesda.Plugins.Binary.Parameters.MastersListOrderingByLoadOrder(PatcherEnvironmentProvider.Instance.Environment.LoadOrder)
            };
            outputMod.WriteToBinary(patchOutputPath, writeParams);
            Logger.LogMessage("Wrote output file at " + patchOutputPath + ".");
        }
        catch (Exception e)
        {
            errStr = ExceptionLogger.GetExceptionStack(e, errStr);
            Logger.LogMessage("Failed to write new patch. Error: " + Environment.NewLine + errStr);
            Logger.LogErrorWithStatusUpdate("Could not write output file to " + patchOutputPath, ErrorType.Error); 
        };
    }
    public static void TryCopyResourceFile(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath))
        {
            Logger.LogErrorWithStatusUpdate("Could not find " + sourcePath, ErrorType.Error);
            return;
        }

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.Copy(sourcePath, destPath, true);
        }
        catch
        {
            Logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
        }
    }

    public static bool TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Logger.LogErrorWithStatusUpdate("Could not delete file - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e, "");
                Logger.LogMessage("Could not delete file: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }

    public static bool TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                Logger.LogErrorWithStatusUpdate("Could not delete directory - see log", ErrorType.Warning);
                string error = ExceptionLogger.GetExceptionStack(e, "");
                Logger.LogMessage("Could not delete directory: " + path + Environment.NewLine + error);
                return false;
            }
        }
        return true;
    }
}