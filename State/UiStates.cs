﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BhModule.Community.Pathing.Entity;
using BhModule.Community.Pathing.UI.Controls;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace BhModule.Community.Pathing.State {
    public class UiStates : ManagedState {

        // Map
        private FlatMap    _map;

        // Info
        private InfoWindow       _info;
        private Label            _infoLabel;
        private SafeList<string> _infoList;

        public UiStates(IRootPackState rootPackState) : base(rootPackState) { /* NOOP */ }

        public override async Task Reload() {
            Unload();

            await Initialize();
        }

        private void InitMap() {
            _map = new FlatMap(_rootPackState) {
                Parent  = GameService.Graphics.SpriteScreen
            };

            GameService.Gw2Mumble.CurrentMap.MapChanged += CurrentMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged   += MapOpenedChanged;
        }

        private void UpdateMapState() {
            if (_map == null) return;

            _map.Visible = !(GameService.Gw2Mumble.UI.IsMapOpen
                                 ? _rootPackState.UserResourceStates.Ignore.Map
                                 : _rootPackState.UserResourceStates.Ignore.Compass).Contains(GameService.Gw2Mumble.CurrentMap.Id);
        }

        private void MapOpenedChanged(object  sender, ValueEventArgs<bool> e) => UpdateMapState();
        private void CurrentMapChanged(object sender, ValueEventArgs<int>  e) => UpdateMapState();

        private void InitInfo() {
            _info = new InfoWindow() {
                Parent  = GameService.Graphics.SpriteScreen,
                Visible = false
            };

            _info.Hide();

            _infoLabel = new Label() {
                Width               = 350,
                WrapText            = true,
                AutoSizeHeight      = true,
                Location            = new Point(70, 60),
                VerticalAlignment   = VerticalAlignment.Middle,
                HorizontalAlignment = HorizontalAlignment.Left,
                Font                = GameService.Content.DefaultFont18,
                Parent              = _info
            };

            _infoList = new SafeList<string>();
        }

        private void UpdateInfoText() {
            string currentInfo = _infoList.LastOrDefault() ?? string.Empty;

            // Add spacing to make font a little more readable.
            _infoLabel.Text = currentInfo.Replace(" ", "  ");

            if (string.IsNullOrEmpty(currentInfo)) {
                _info.Hide();
            } else {
                _info.Show();
            }

        }

        public void AddInfoString(string info) {
            _infoList.Add(info);

            UpdateInfoText();
        }

        public void RemoveInfoString(string info) {
            _infoList.Remove(info);

            UpdateInfoText();
        }

        protected override Task<bool> Initialize() {
            InitInfo();
            InitMap();

            return Task.FromResult(true);
        }

        protected override void Unload() {
            GameService.Gw2Mumble.CurrentMap.MapChanged -= CurrentMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged   -= MapOpenedChanged;
        }

        public override void Update(GameTime gameTime) { /* NOOP */ }

    }
}
