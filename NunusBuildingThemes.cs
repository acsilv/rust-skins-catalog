using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NunusBuildingThemes", "Rust dos Nunus", "3.0.0")]
    [Description("Building theme selector with controlled, time-limited testing on real upgrades.")]
    public class NunusBuildingThemes : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;
        private const string UsePerm = "nunusbuildingthemes.use";
        private const string AdminPerm = "nunusbuildingthemes.admin";
        private const string Root = "NunusBuildingThemes.UI";
        private const int PageSize = 6;
        private Settings settings;
        private Store store;
        private readonly Dictionary<ulong, string> tabs = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> pages = new Dictionary<ulong, int>();
        private DateTime testingUntil = DateTime.MinValue;

        private class Theme
        {
            [JsonProperty("Grade")] public string Grade;
            [JsonProperty("Name")] public string Name;
            [JsonProperty("SkinId")] public ulong SkinId;
            [JsonProperty("PreviewImageUrl")] public string PreviewImageUrl;
            [JsonProperty("PreviewImageKey")] public string PreviewImageKey;
        }
        private class Settings
        {
            [JsonProperty("EnableTemporaryTestApplication (admin only)")]
            public bool EnableTemporaryTestApplication = false;
            [JsonProperty("MaximumTestMinutes")] public int MaximumTestMinutes = 30;
            [JsonProperty("Themes")] public List<Theme> Themes = Defaults();
        }
        private class Store
        {
            public Dictionary<ulong, Dictionary<string, ulong>> Preferences = new Dictionary<ulong, Dictionary<string, ulong>>();
        }
        private static List<Theme> Defaults()
        {
            return new List<Theme>
            {
                new Theme { Grade="Wood", Name="Padrão", SkinId=0 },
                new Theme { Grade="Wood", Name="Frontier", SkinId=10232 },
                new Theme { Grade="Stone", Name="Padrão", SkinId=0 },
                new Theme { Grade="Stone", Name="Adobe", SkinId=10220 },
                new Theme { Grade="Stone", Name="Brick", SkinId=10223 },
                new Theme { Grade="Stone", Name="Brutalist", SkinId=10225 },
                new Theme { Grade="Stone", Name="Jungle", SkinId=10326 },
                new Theme { Grade="Metal", Name="Padrão", SkinId=0 },
                new Theme { Grade="Metal", Name="Shipping Container", SkinId=10221 },
                new Theme { Grade="TopTier", Name="Padrão", SkinId=0 },
                new Theme { Grade="TopTier", Name="Space Station", SkinId=10430 }
            };
        }
        protected override void LoadDefaultConfig()
        {
            settings = new Settings();
            Config.WriteObject(settings, true);
        }
        private void Init()
        {
            permission.RegisterPermission(UsePerm, this);
            permission.RegisterPermission(AdminPerm, this);
            ReadSettings();
            try { store = Interface.Oxide.DataFileSystem.ReadObject<Store>(Name); }
            catch { store = new Store(); }
            if (store == null || store.Preferences == null) store = new Store();
        }
        private void ReadSettings()
        {
            try { settings = Config.ReadObject<Settings>(); }
            catch { PrintWarning("Invalid config; default themes loaded (config not overwritten)."); settings = new Settings(); }
            if (settings == null) settings = new Settings();
            if (settings.Themes == null) settings.Themes = Defaults();
            settings.MaximumTestMinutes = Math.Max(1, Math.Min(60, settings.MaximumTestMinutes));
            // Avoid duplicates, invalid grades and unfinished placeholders.
            settings.Themes = settings.Themes
                .Where(t => t != null && (t.Grade == "Wood" || t.Grade == "Stone" || t.Grade == "Metal" || t.Grade == "TopTier")
                    && !String.IsNullOrWhiteSpace(t.Name) && (t.SkinId != 0 || t.Name == "Padrão"))
                .GroupBy(t => t.Grade + "|" + t.SkinId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
            foreach (var grade in new[] { "Wood", "Stone", "Metal", "TopTier" })
                if (!settings.Themes.Any(t => t.Grade == grade && t.SkinId == 0))
                    settings.Themes.Insert(0, new Theme { Grade = grade, Name = "Padrão", SkinId = 0 });
        }
        private void OnServerInitialized() { RegisterImages(); }
        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList) CuiHelper.DestroyUi(player, Root);
            Interface.Oxide.DataFileSystem.WriteObject(Name, store);
            testingUntil = DateTime.MinValue;
        }
        private bool CanUse(BasePlayer p) { return p != null && p.IsConnected && permission.UserHasPermission(p.UserIDString, UsePerm); }
        private bool Admin(BasePlayer p) { return p != null && permission.UserHasPermission(p.UserIDString, AdminPerm); }
        private bool Testing() { return settings.EnableTemporaryTestApplication && DateTime.UtcNow < testingUntil; }
        private static string GradeKey(BuildingGrade.Enum grade)
        {
            switch (grade)
            {
                case BuildingGrade.Enum.Wood: return "Wood";
                case BuildingGrade.Enum.Stone: return "Stone";
                case BuildingGrade.Enum.Metal: return "Metal";
                case BuildingGrade.Enum.TopTier: return "TopTier";
                default: return null;
            }
        }
        private ulong Preference(ulong id, string grade)
        {
            Dictionary<string, ulong> values;
            ulong selected;
            return store.Preferences.TryGetValue(id, out values) && values.TryGetValue(grade, out selected) ? selected : 0;
        }
        private void SetPreference(ulong id, string grade, ulong skin)
        {
            Dictionary<string, ulong> values;
            if (!store.Preferences.TryGetValue(id, out values))
            {
                values = new Dictionary<string, ulong>();
                store.Preferences[id] = values;
            }
            values[grade] = skin;
            Interface.Oxide.DataFileSystem.WriteObject(Name, store);
        }
        private Theme FindTheme(string grade, ulong id)
        {
            return settings.Themes.FirstOrDefault(t => t.Grade == grade && t.SkinId == id);
        }
        private void RegisterImages()
        {
            if (ImageLibrary == null) return;
            foreach (var t in settings.Themes)
            {
                if (String.IsNullOrWhiteSpace(t.PreviewImageUrl) ||
                    !Uri.IsWellFormedUriString(t.PreviewImageUrl, UriKind.Absolute)) continue;
                string key = Key(t);
                try { ImageLibrary.Call("AddImage", t.PreviewImageUrl, key, (ulong)0); }
                catch (Exception e) { PrintWarning("Preview " + t.Name + ": " + e.Message); }
            }
        }
        private string Key(Theme t)
        {
            return !String.IsNullOrWhiteSpace(t.PreviewImageKey)
                ? t.PreviewImageKey : "nunusbuild_" + t.Grade.ToLowerInvariant() + "_" + t.SkinId;
        }
        [ChatCommand("nunusbuild")]
        private void Open(BasePlayer player, string cmd, string[] args)
        {
            if (!CanUse(player)) { SendReply(player, "Sem permissão para NunusBuild."); return; }
            tabs[player.userID] = "Wood";
            pages[player.userID] = 0;
            Draw(player);
        }
        // Public API for NunusHub (direct plugin call, no chat injection).
        [HookMethod("OpenFor")]
        public void OpenFor(BasePlayer player)
        {
            if (player != null) Open(player, "nunusbuild", new string[0]);
        }
        [ConsoleCommand("nunusbuild.action")]
        private void Action(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player) || arg.Args == null || arg.Args.Length == 0) return;
            string action = arg.Args[0].ToString();
            if (action == "close") { CuiHelper.DestroyUi(player, Root); return; }
            string grade;
            if (!tabs.TryGetValue(player.userID, out grade)) return;
            if (action == "tab" && arg.Args.Length == 2)
            {
                string next = arg.Args[1].ToString();
                if (next != "Wood" && next != "Stone" && next != "Metal" && next != "TopTier") return;
                tabs[player.userID] = next; pages[player.userID] = 0;
            }
            else if (action == "page" && arg.Args.Length == 2)
            {
                int delta;
                if (!int.TryParse(arg.Args[1].ToString(), out delta) || Math.Abs(delta) != 1) return;
                pages[player.userID] = Math.Max(0, pages[player.userID] + delta);
            }
            else if (action == "pick" && arg.Args.Length == 2)
            {
                ulong skin;
                if (!ulong.TryParse(arg.Args[1].ToString(), out skin)) return;
                if (FindTheme(grade, skin) == null) return;
                SetPreference(player.userID, grade, skin);
                SendReply(player, "Tema escolhido: " + FindTheme(grade, skin).Name +
                    (Testing() && Admin(player) ? " (teste ativo; próximo upgrade será estilizado)" :
                        " (pré-selecionado; aplicação só durante teste administrativo ativo)"));
            }
            Draw(player);
        }
        [ConsoleCommand("nunusbuild.test")]
        private void Test(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !Admin(player)) return;
            if (arg.Args == null || arg.Args.Length != 1) return;
            int minutes;
            if (!int.TryParse(arg.Args[0].ToString(), out minutes)) return;
            if (minutes <= 0) { testingUntil = DateTime.MinValue; SendReply(player, "Teste encerrado."); return; }
            if (!settings.EnableTemporaryTestApplication)
            {
                SendReply(player, "Habilite EnableTemporaryTestApplication no JSON primeiro."); return;
            }
            int allowed = Math.Min(minutes, settings.MaximumTestMinutes);
            testingUntil = DateTime.UtcNow.AddMinutes(allowed);
            SendReply(player, "Teste de construção ativo por " + allowed + " minutos (apenas admin). Use /nunusbuild para escolher.");
        }
        // Called AFTER a normal successful upgrade; regular material cost remains intact.
        private void OnStructureUpgraded(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong originalSkin)
        {
            if (block == null || player == null || !Admin(player) || !Testing()) return;
            string key = GradeKey(grade);
            if (key == null) return;
            ulong desired = Preference(player.userID, key);
            if (desired == 0 || FindTheme(key, desired) == null) return;
            ulong blockId = block.net != null ? block.net.ID.Value : 0UL;
            NextTick(() =>
            {
                if (block == null || block.IsDestroyed || player == null) return;
                if (GradeKey(block.grade) != key) return;
                if (!Testing()) return;
                block.skinID = desired;
                block.UpdateSkin();
                block.SendNetworkUpdate();
                Puts("Applied building theme " + desired + " (" + key + ") to block " + blockId + " for " + player.userID);
            });
        }
        private static string Coord(float x) { return x.ToString("0.####", CultureInfo.InvariantCulture); }
        private static string XY(float x, float y) { return Coord(x) + " " + Coord(y); }
        private void Label(CuiElementContainer c, string parent, string value, string min, string max, int size = 13)
        {
            c.Add(new CuiLabel { Text = { Text = value, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "0.96 0.97 1 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Button(CuiElementContainer c, string parent, string value, string command, string min, string max, string color = "0.19 0.29 0.41 1")
        {
            c.Add(new CuiButton { Button = { Color = color, Command = command }, Text = { Text = value, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Draw(BasePlayer player)
        {
            if (!CanUse(player)) return;
            string grade;
            if (!tabs.TryGetValue(player.userID, out grade)) return;
            CuiHelper.DestroyUi(player, Root);
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.06 0.09 0.13 0.96" }, RectTransform = { AnchorMin = "0.12 0.12", AnchorMax = "0.88 0.88" }, CursorEnabled = true }, "Overlay", Root);
            Label(c, Root, "NUNUS BUILD  |  TEMAS DE CONSTRUÇÃO", "0.04 0.91", "0.86 0.99", 19);
            Button(c, Root, "X", "nunusbuild.action close", "0.92 0.92", "0.98 0.98", "0.58 0.15 0.17 1");
            string[] grades = { "Wood", "Stone", "Metal", "TopTier" };
            string[] labels = { "MADEIRA", "PEDRA", "METAL", "BLINDADO" };
            for (int i = 0; i < grades.Length; i++)
            {
                float x = 0.03f + i * 0.246f;
                Button(c, Root, labels[i], "nunusbuild.action tab " + grades[i], XY(x, 0.835f), XY(x + 0.23f, 0.90f), grade == grades[i] ? "0.19 0.43 0.66 1" : "0.17 0.25 0.33 1");
            }
            var options = settings.Themes.Where(t => t.Grade == grade).ToList();
            int count = Math.Max(1, (options.Count + PageSize - 1) / PageSize);
            int current = Math.Min(Math.Max(0, pages.ContainsKey(player.userID) ? pages[player.userID] : 0), count - 1);
            pages[player.userID] = current;
            ulong active = Preference(player.userID, grade);
            for (int i = 0; i < PageSize; i++)
            {
                int idx = current * PageSize + i;
                if (idx >= options.Count) break;
                Theme t = options[idx];
                int col = i % 3, row = i / 3;
                float x = 0.032f + col * 0.327f;
                float top = 0.79f - row * 0.34f;
                string tile = c.Add(new CuiPanel { Image = { Color = "0.12 0.17 0.23 1" }, RectTransform = { AnchorMin = XY(x, top - 0.32f), AnchorMax = XY(x + 0.308f, top) } }, Root);
                string image = null;
                if (ImageLibrary != null && !String.IsNullOrWhiteSpace(t.PreviewImageUrl))
                {
                    try { image = ImageLibrary.Call("GetImage", Key(t), (ulong)0) as string; }
                    catch { }
                }
                if (!String.IsNullOrEmpty(image))
                    c.Add(new CuiElement { Parent = tile, Components = { new CuiRawImageComponent { Png = image, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.06 0.29", AnchorMax = "0.94 0.94" } } });
                else Label(c, tile, String.IsNullOrWhiteSpace(t.PreviewImageUrl) ? "Sem prévia cadastrada" : "Imagem carregando", "0.05 0.36", "0.95 0.82", 12);
                Label(c, tile, t.Name + (active == t.SkinId ? "  [SELECIONADO]" : ""), "0.03 0.16", "0.97 0.28", 11);
                Button(c, tile, "SELECIONAR", "nunusbuild.action pick " + t.SkinId, "0.12 0.02", "0.88 0.15", "0.13 0.39 0.27 1");
            }
            Button(c, Root, "ANTERIOR", "nunusbuild.action page -1", "0.28 0.035", "0.44 0.09");
            Label(c, Root, "Página " + (current + 1) + "/" + count, "0.45 0.035", "0.55 0.09", 13);
            Button(c, Root, "PRÓXIMA", "nunusbuild.action page 1", "0.56 0.035", "0.72 0.09");
            Label(c, Root, Testing() ? "TESTE ATIVO — upgrades de administradores" : "Aplicação desativada — habilite sessão de testes", "0.03 0.003", "0.97 0.03", 10);
            CuiHelper.AddUi(player, c);
        }
    }
}
