﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BhModule.Community.Pathing.State;
using BhModule.Community.Pathing.UI.Controls;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TmfLib;
using TmfLib.Reader;

namespace BhModule.Community.Pathing {
    public class PackInitiator : IUpdatable {

        private static readonly Logger Logger = Logger.GetLogger<PackInitiator>();

        private readonly string            _watchPath;
        private readonly ModuleSettings    _moduleSettings;
        private readonly IProgress<string> _loadingIndicator;

        private readonly IRootPackState _packState;

        private readonly SafeList<Pack> _packs = new();

        private SharedPackCollection _sharedPackCollection;
        
        private readonly PackReaderSettings _packReaderSettings;

        private bool _isLoading = false;
        private int  _lastMap   = -1;

        public PackInitiator(string watchPath, ModuleSettings moduleSettings, IProgress<string> loadingIndicator) {
            _watchPath        = watchPath;
            _moduleSettings   = moduleSettings;
            _loadingIndicator = loadingIndicator;

            _packReaderSettings = new PackReaderSettings();
            _packReaderSettings.VenderPrefixes.Add("bh-"); // Support Blish HUD specific categories/markers/trails/attributes.

            _packState = new SharedPackState(moduleSettings);
        }

        public IEnumerable<ContextMenuStripItem> GetPackMenuItems() {
            // All Markers
            bool isAnyMarkers = !_isLoading
                             && _sharedPackCollection != null
                             && _sharedPackCollection.Categories != null
                             && _sharedPackCollection.Categories.Any(category => !string.IsNullOrWhiteSpace(category.DisplayName));

            var allMarkers = new ContextMenuStripItem() {
                Text = "All Markers", // TODO: Localize "All Markers"
                CanCheck = true,
                Checked = _moduleSettings.GlobalPathablesEnabled.Value,
                Submenu = isAnyMarkers
                    ? new CategoryContextMenuStrip(_packState, _sharedPackCollection.Categories, false)
                    : null
            };

            allMarkers.CheckedChanged += (_, e) => {
                _moduleSettings.GlobalPathablesEnabled.Value = e.Checked;
            };

            // Reload Markers
            var reloadMarkers = new ContextMenuStripItem() {
                Text    = "Reload Markers", // TODO: Localize "Reload Markers"
                Enabled = !_isLoading && _packState.CurrentMapId > 0
            };

            reloadMarkers.Click += (_, _) => {
                if (_packState.CurrentMapId < 0) return;

                _lastMap = -1;

                LoadMapFromEachPackInBackground(_packState.CurrentMapId);
            };

            // Unload Markers
            var unloadMarkers = new ContextMenuStripItem() {
                Text    = "Unload Markers", // TODO: Localize "Unload Markers"
                Enabled = !_isLoading && _packState.CurrentMapId > 0
            };

            unloadMarkers.Click += async (_, _) => {
                if (_packState.CurrentMapId < 0) return;

                await UnloadStateAndCollection();
            };
            
            yield return allMarkers;
            yield return reloadMarkers;
            yield return unloadMarkers;
        }

        public async Task Init() {
            await _packState.Load();
            await LoadAllPacks();
        }

        private async Task LoadUnpackedPackFiles(string unpackedDir) {
            var newPack = Pack.FromDirectoryMarkerPack(unpackedDir);
            
            _packs.Add(newPack);
        }

        private async Task LoadPackedPackFiles(IEnumerable<string> zipPackFiles) {
            foreach (var newPack in zipPackFiles.Select(Pack.FromArchivedMarkerPack)) {
                _packs.Add(newPack);
            }
        }

        private async Task UnloadStateAndCollection() {
            _sharedPackCollection?.Unload();
            await _packState.Unload();
        }

        private async Task LoadAllPacks() {
            // Load from base and advanced markers paths
            foreach (string markerDir in (_packState.UserResourceStates.Advanced.MarkerLoadPaths ?? Array.Empty<string>()).Concat(new [] {_watchPath})) {
                await LoadPackedPackFiles(Directory.GetFiles(markerDir, "*.zip",  SearchOption.AllDirectories));
                await LoadPackedPackFiles(Directory.GetFiles(markerDir, "*.taco", SearchOption.AllDirectories));
                await LoadUnpackedPackFiles(markerDir);
            }

            // If the module loads at launch, this can end up firing twice.
            // If the module loads after launch (manually enabled), we need this to populate the current map.
            if (GameService.Gw2Mumble.CurrentMap.Id != default) {
                LoadMapFromEachPackInBackground(_packState.CurrentMapId = GameService.Gw2Mumble.CurrentMap.Id);
            }

            GameService.Gw2Mumble.CurrentMap.MapChanged += OnMapChanged;
        }

        private async Task PrepareState(int mapId) {
            await UnloadStateAndCollection();

            _sharedPackCollection = new SharedPackCollection();
        }

        private void LoadMapFromEachPackInBackground(int mapId) {
            lock (this) {
                if (mapId == _lastMap) {
                    return;
                };
                _lastMap = mapId;
            }

            var thread = new Thread(async () => await LoadMapFromEachPack(mapId)) {
                IsBackground = true
            };

            thread.Start();
        }

        private async Task LoadMapFromEachPack(int mapId) {
            _isLoading = true;

            var loadTimer = Stopwatch.StartNew();

            // TODO: Localize the loading messages.
            _loadingIndicator.Report("Loading marker packs...");

            await PrepareState(mapId);

            Pack lastPack = null;

            try {
                foreach (var pack in _packs.ToArray()) {
                    lastPack = pack;
                    _loadingIndicator.Report($"Loading {pack.Name}...");
                    await pack.LoadMapAsync(mapId, _sharedPackCollection, _packReaderSettings);
                }
            } catch (FileNotFoundException e) {
                Logger.Warn("Pack file '{packPath}' failed to load because it could not be found.", e.FileName);

                if (lastPack != null) {
                    _packs.Remove(lastPack);
                }
            } catch (Exception e) {
                Logger.Warn(e, "Loading pack failed.");

                if (lastPack != null) {
                    _packs.Remove(lastPack);
                }
            }

            _loadingIndicator.Report("Finalizing marker collection...");
            await _packState.LoadPackCollection(_sharedPackCollection);

            foreach (var pack in _packs.ToArray()) {
                pack.ReleaseLocks();
            }

            _loadingIndicator.Report("");

            _isLoading = false;

            loadTimer.Stop();
            Logger.Info($"Finished loading packs {string.Join(", ", _packs.Select(pack => pack.Name))} in {loadTimer.ElapsedMilliseconds} ms for map {mapId}.");
        }

        private void OnMapChanged(object sender, ValueEventArgs<int> e) {
            if (e.Value == _packState.CurrentMapId) return;

            _packState.CurrentMapId = e.Value;

            LoadMapFromEachPackInBackground(e.Value);
        }
        
        public void Update(GameTime gameTime) {
            _packState.Update(gameTime);
        }

        public void Unload() {
            GameService.Gw2Mumble.CurrentMap.MapChanged -= OnMapChanged;

            _packState.Unload();
        }

    }
}
