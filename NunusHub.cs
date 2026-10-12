using System;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NunusHub", "Rust dos Nunus", "1.0.0")]
    [Description("Painel flutuante para abrir os plugins do Rust dos Nunus.")]
    public class NunusHub : RustPlugin
    {
        [PluginReference] private Plugin NunusSkinsUI;
        [PluginReference] private Plugin NunusBuildingThemes;
        [PluginReference] private Plugin NunusDecor;

        private const string PermissionUse = "nunushub.use";
        private const string HudName = "NunusHub.Hud";
        private const string MenuName = "NunusHub.Menu";
        private readonly System.Collections.Generic.HashSet<ulong> opened = new System.Collections.Generic.HashSet<ulong>();

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        private void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DrawHud(player);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(3f, () => { if (player != null && player.IsConnected) DrawHud(player); });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null) opened.Remove(player.userID);
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, MenuName);
                CuiHelper.DestroyUi(player, HudName);
            }
            opened.Clear();
        }

        private bool Allowed(BasePlayer player)
        {
            return player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, PermissionUse);
        }

        [ChatCommand("hub")]
        private void HubChat(BasePlayer player, string cmd, string[] args)
        {
            Toggle(player);
        }

        [ChatCommand("nunushub")]
        private void NunusHubChat(BasePlayer player, string cmd, string[] args)
        {
            Toggle(player);
        }

        [ConsoleCommand("nunushub.toggle")]
        private void ToggleConsole(ConsoleSystem.Arg arg)
        {
            Toggle(arg.Player());
        }

        private void Toggle(BasePlayer player)
        {
            if (!Allowed(player)) return;
            if (opened.Contains(player.userID)) Close(player);
            else Open(player);
        }

        private void Close(BasePlayer player)
        {
            if (player == null) return;
            opened.Remove(player.userID);
            CuiHelper.DestroyUi(player, MenuName);
        }

        private void DrawHud(BasePlayer player)
        {
            if (!Allowed(player)) return;
            CuiHelper.DestroyUi(player, HudName);
            var ui = new CuiElementContainer();
            ui.Add(new CuiButton
            {
                Button = { Color = "0.12 0.30 0.50 0.91", Command = "nunushub.toggle" },
                Text = { Text = "NUNUS", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.918 0.46", AnchorMax = "0.991 0.51" }
            }, "Overlay", HudName);
            CuiHelper.AddUi(player, ui);
        }

        private void Open(BasePlayer player)
        {
            Close(player);
            opened.Add(player.userID);
            var ui = new CuiElementContainer();
            ui.Add(new CuiPanel
            {
                Image = { Color = "0.055 0.09 0.14 0.97" },
                RectTransform = { AnchorMin = "0.30 0.23", AnchorMax = "0.70 0.77" },
                CursorEnabled = true
            }, "Overlay", MenuName);
            ui.Add(new CuiLabel
            {
                Text = { Text = "RUST DOS NUNUS  |  HUB", FontSize = 22, Color = "0.95 0.97 1 1", Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.08 0.84", AnchorMax = "0.85 0.98" }
            }, MenuName);
            AddButton(ui, "X", "nunushub.action close", "0.89 0.88", "0.97 0.97", "0.60 0.16 0.19 1");
            AddButton(ui, "SKINS  |  armas e roupas", "nunushub.action skins", "0.10 0.63", "0.90 0.77", NunusSkinsUI != null ? "0.12 0.37 0.55 1" : "0.24 0.26 0.29 1");
            AddButton(ui, "CONSTRUÇÃO  |  temas", "nunushub.action build", "0.10 0.44", "0.90 0.58", NunusBuildingThemes != null ? "0.12 0.37 0.55 1" : "0.24 0.26 0.29 1");
            AddButton(ui, "DECORAÇÃO  |  em breve", "nunushub.action decor", "0.10 0.25", "0.90 0.39", NunusDecor != null ? "0.12 0.37 0.55 1" : "0.24 0.26 0.29 1");
            ui.Add(new CuiLabel
            {
                Text = { Text = "Clique em um módulo para abrir. /hub também funciona.", FontSize = 11, Color = "0.65 0.75 0.84 1", Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.16" }
            }, MenuName);
            CuiHelper.AddUi(player, ui);
        }

        private void AddButton(CuiElementContainer ui, string title, string command, string min, string max, string color)
        {
            ui.Add(new CuiButton
            {
                Button = { Color = color, Command = command },
                Text = { Text = title, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, MenuName);
        }

        [ConsoleCommand("nunushub.action")]
        private void Action(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!Allowed(player) || arg.Args == null || arg.Args.Length != 1 || !opened.Contains(player.userID)) return;
            string action = arg.Args[0].ToString();
            if (action == "close") { Close(player); return; }
            Plugin target = null;
            if (action == "skins") target = NunusSkinsUI;
            else if (action == "build") target = NunusBuildingThemes;
            else if (action == "decor") target = NunusDecor;
            else return;
            if (target == null)
            {
                SendReply(player, "Este módulo ainda não está instalado no servidor.");
                return;
            }
            Close(player);
            object result = target.Call("NunusHub_Open", player);
            // Módulos antigos precisam do hook NunusHub_Open descrito no instalador.
            if (result is string) SendReply(player, (string)result);
        }
    }
}
