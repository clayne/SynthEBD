using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class EBDScripts
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        public EBDScripts(IEnvironmentStateProvider environmentProvider, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _environmentProvider = environmentProvider;
            _logger = logger;
            _paths = paths;
            _patcherIO = patcherIO; 
        }
        public void ApplyFixedScripts()
        {
            string sourcePath = String.Empty;
            if ((_environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE && !PatcherSettings.TexMesh.bFixedScriptsOldSKSEversion) || _environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.EnderalSE)
            {
                _logger.LogMessage("Applying fixed EBD script (for SSE 1.5.97 or newer)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "SSE", "EBDGlobalFuncs.pex");
            }
            else
            {
                _logger.LogMessage("Applying fixed EBD script (for VR or SSE < 1.5.97)");
                sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "VR", "EBDGlobalFuncs.pex");
            }
            string destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "EBDGlobalFuncs.pex");
            _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
        }
    }
}
