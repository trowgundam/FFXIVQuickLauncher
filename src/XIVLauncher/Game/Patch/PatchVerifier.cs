using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common;
using XIVLauncher.PatchInstaller.IndexedZiPatch;

namespace XIVLauncher.Game.Patch
{
    public class PatchVerifier : IDisposable
    {
        private IndexedZiPatchIndexRemoteInstaller _remote;
        private HttpClient _client;
        private CancellationTokenSource _cancellationTokenSource = new();

        private Dictionary<Repository, string> _repoMetaPaths = new();
        private Dictionary<string, string> _patchSources = new();

        private const string BASE_URL = "https://raw.githubusercontent.com/goatcorp/patchinfo/main/";

        public enum VerifyState
        {
            Unknown,
            Verify,
            Done,
            Cancelled
        }

        public VerifyState State { get; private set; } = VerifyState.Unknown;

        public PatchVerifier()
        {
            var assemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _remote = new IndexedZiPatchIndexRemoteInstaller(Path.Combine(assemblyLocation!, "XIVLauncher.PatchInstaller.exe"),
                true);
            _client = new HttpClient();
        }

        public void Start()
        {
            Debug.Assert(_repoMetaPaths.Count != 0 && _patchSources.Count != 0);

            Task.Run(this.RunVerifier, _cancellationTokenSource.Token);
        }

        public void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        public void SetLoginState(Launcher.LoginResult result)
        {
            _patchSources.Clear();
            Debug.Assert(result.PendingPatches.Length != 0);

            foreach (var patch in result.PendingPatches)
            {
                var repoName = patch.GetRepoName();
                if (repoName == "ffxiv")
                    repoName = "ex0";

                _patchSources.Add($"{repoName}:{Path.GetFileName(patch.GetFilePath())}", patch.Url);
            }
        }

        private async Task RunVerifier()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                switch (State)
                {

                    case VerifyState.Unknown:
                        State = VerifyState.Verify;
                        break;
                    case VerifyState.Verify:
                        const int maxConcurrentConnectionsForPatchSet = 8;

                        foreach (var metaPath in this._repoMetaPaths)
                        {
                            var patchIndex = new IndexedZiPatchIndex(new BinaryReader(new DeflateStream(new FileStream(metaPath.Value, FileMode.Open, FileAccess.Read), CompressionMode.Decompress)));

                            await _remote.ConstructFromPatchFile(patchIndex);

                            var adjustedGamePath = Path.Combine(App.Settings.GamePath.FullName, patchIndex.ExpacVersion == IndexedZiPatchIndex.EXPAC_VERSION_BOOT ? "boot" : "game");

                            void ReportCheckProgress(int index, long progress, long max)
                            {
                                Log.Information("[{0}/{1}] Checking file {2}... {3:0.00}/{4:0.00}MB ({5:00.00}%)", index + 1, patchIndex.Length, patchIndex[Math.Min(index, patchIndex.Length - 1)].RelativePath, progress / 1048576.0, max / 1048576.0, 100.0 * progress / max);
                            }

                            _remote.OnProgress += ReportCheckProgress;
                            await _remote.SetTargetStreamsFromPathReadOnly(adjustedGamePath);
                            // TODO: check one at a time if random access is slow?
                            await _remote.VerifyFiles(Environment.ProcessorCount, this._cancellationTokenSource.Token);
                            _remote.OnProgress -= ReportCheckProgress;

                            var missing = await _remote.GetMissingPartIndicesPerPatch();

                            void ReportInstallProgress(int index, long progress, long max)
                            {
                                Log.Information("[{0}/{1}] Installing {2}... {3:0.00}/{4:0.00}MB ({5:00.00}%)", index + 1, patchIndex.Sources.Count, patchIndex.Sources[Math.Min(index, patchIndex.Sources.Count - 1)], progress / 1048576.0, max / 1048576.0, 100.0 * progress / max);
                            }

                            _remote.OnProgress += ReportInstallProgress;
                            await _remote.SetTargetStreamsFromPathReadWriteForMissingFiles(adjustedGamePath);
                            var prefix = patchIndex.ExpacVersion == IndexedZiPatchIndex.EXPAC_VERSION_BOOT ? "boot:" : $"ex{patchIndex.ExpacVersion}:";
                            for (var i = 0; i < patchIndex.Sources.Count; i++)
                            {
                                if (!missing[i].Any())
                                    continue;

                                await _remote.QueueInstall(i, _patchSources[prefix + patchIndex.Sources[i]], null, maxConcurrentConnectionsForPatchSet);
                            }
                            await _remote.Install(maxConcurrentConnectionsForPatchSet, this._cancellationTokenSource.Token);
                            await _remote.WriteVersionFiles(adjustedGamePath);
                            _remote.OnProgress -= ReportInstallProgress;
                        }

                        State = VerifyState.Done;
                        break;
                    case VerifyState.Done:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public async Task GetPatchMeta()
        {
            _repoMetaPaths.Clear();

            var metaFolder = Path.Combine(Paths.RoamingPath, "patchMeta");
            Directory.CreateDirectory(metaFolder);

            await this.GetRepoMeta(Repository.Ffxiv, metaFolder);
            await this.GetRepoMeta(Repository.Ex1, metaFolder);
            await this.GetRepoMeta(Repository.Ex2, metaFolder);
            await this.GetRepoMeta(Repository.Ex3, metaFolder);
            await this.GetRepoMeta(Repository.Ex4, metaFolder);
        }

        private async Task GetRepoMeta(Repository repo, string baseDir)
        {
            var version = repo.GetVer(App.Settings.GamePath);
            if (version == Constants.BASE_GAME_VERSION)
                return;

            var repoShorthand = repo == Repository.Ffxiv ? "game" : repo.ToString().ToLower();
            var fileName = $"D{version}.patch.index";

            var metaPath = Path.Combine(baseDir, repoShorthand);
            var filePath = Path.Combine(metaPath, fileName);
            Directory.CreateDirectory(metaPath);

            if (!File.Exists(filePath))
            {
                var request = await _client.GetAsync($"{BASE_URL}{repoShorthand}/{fileName}", _cancellationTokenSource.Token);
                if (request.StatusCode == HttpStatusCode.NotFound)
                    throw new NoVersionReferenceException(repo, version);

                request.EnsureSuccessStatusCode();

                File.WriteAllBytes(filePath, await request.Content.ReadAsByteArrayAsync());
            }

            _repoMetaPaths.Add(repo, filePath);
            Log.Verbose("Downloaded patch index for {Repo}({Version})", repo, version);
        }

        public void Dispose()
        {
            this._remote?.Dispose();
        }
    }
}