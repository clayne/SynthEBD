using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class CommonScripts
    {
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        private readonly Logger _logger;
        public CommonScripts(SynthEBDPaths paths, PatcherIO patcherIO, Logger logger)
        {
            _paths = paths;
            _patcherIO = patcherIO;
            _logger = logger;
        }
        public void CopyAllToOutputFolder()
        {
            string sourcePath1 = Path.Combine(_paths.ResourcesFolderPath, "Common Scripts", "SynthEBDcLib.pex");
            string destPath1 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDcLib.pex");
            _patcherIO.TryCopyResourceFile(sourcePath1, destPath1, _logger);

            string sourcePath2 = Path.Combine(_paths.ResourcesFolderPath, "Common Scripts", "SynthEBDCommonFuncs.pex");
            string destPath2 = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDCommonFuncs.pex");
            _patcherIO.TryCopyResourceFile(sourcePath2, destPath2, _logger);
        }
    }
}
