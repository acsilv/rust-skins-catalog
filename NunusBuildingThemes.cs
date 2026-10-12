using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NunusBuildingThemes", "Rust dos Nunus", "2.1.0")]
    [Description("Visual construction theme selector with separately enabled, time-limited mechanics testing.")]
    public class NunusBuildingThemes : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;
        private const string PermUse = "nunusbuildingthemes.use";
        private const string PermAdmin = "nunusbuildingthemes.admin";
        private const string Root = "NunusBuildingThemes.Root";
        private readonly Dictionary<ulong, string> selectedGrade = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> page = new Dictionary<ulong, int>();
        private Data saved;
        private Settings settings;
        private DateTime testingUntilUtc = DateTime.MinValue;
        private readonly HashSet<string> queuedPreviews = new HashSet<string>();

        private class Theme
        {
            [JsonProperty("Name")] public string Name;
            [JsonProperty("Grade")] public string Grade;
            [JsonProperty("SkinId")] public ulong SkinId;
            [JsonProperty("PreviewImageKey")] public string PreviewImageKey;
            [JsonProperty("PreviewImageUrl")] public string PreviewImageUrl;
        }
        private class Settings
        {
            [JsonProperty("Themes")] public List<Theme> Themes = new List<Theme>
            {
                new Theme {Name="Padrão", Grade="Wood", SkinId=0},
                new Theme {Name="Legacy Wood (configure ID)", Grade="Wood", SkinId=0},
                new Theme {Name="Padrão", Grade="Stone", SkinId=0},
                new Theme {Name="Adobe", Grade="Stone", SkinId=10220},
                new Theme {Name="Brick", Grade="Stone", SkinId=10223},
                new Theme {Name="Brutalist", Grade="Stone", SkinId=10225},
                new Theme {Name="Jungle (configure ID)", Grade="Stone", SkinId=0},
                new Theme {Name="Crypt (configure ID)", Grade="Stone", SkinId=0},
                new Theme {Name="Padrão", Grade="Metal", SkinId=0},
                new Theme {Name="Shipping Container", Grade="Metal", SkinId=10221},
                new Theme {Name="Padrão", Grade="TopTier", SkinId=0},
                new Theme {Name="Space Station (configure ID)", Grade="TopTier", SkinId=0}
            };
            [JsonProperty("EnableTemporaryTestApplication (admin only)")] public bool EnableTemporaryTestApplication = false;
            [JsonProperty("MaximumTestMinutes")] public int MaximumTestMinutes = 30;
        }
        private class Data
        {
            public Dictionary<ulong, Dictionary<string, ulong>> Preferences = new Dictionary<ulong, Dictionary<string, ulong>>();
        }

        protected override void LoadDefaultConfig() { settings = new Settings(); Config.WriteObject(settings, true); }
        private void LoadSettings()
        {
            try { settings = Config.ReadObject<Settings>(); }
            catch { PrintWarning("Invalid config, using defaults."); settings = new Settings(); }
            if (settings == null || settings.Themes == null) settings = new Settings();
            settings.MaximumTestMinutes = Math.Max(1, Math.Min(60, settings.MaximumTestMinutes));
        }
        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            LoadSettings();
            try { saved = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name); }
            catch { saved = new Data(); }
            if (saved == null || saved.Preferences == null) saved = new Data();
        }
        private void OnServerInitialized() { timer.Once(3f, RegisterPreviews); }
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary")
                timer.Once(2f, RegisterPreviews);
        }
        private void RegisterPreviews()
        {
            if (ImageLibrary == null || settings == null || settings.Themes == null) return;
            foreach (var theme in settings.Themes)
            {
                if (theme == null || String.IsNullOrWhiteSpace(theme.PreviewImageUrl)) continue;
                if (!Uri.IsWellFormedUriString(theme.PreviewImageUrl, UriKind.Absolute)) continue;
                Uri url;
                if (!Uri.TryCreate(theme.PreviewImageUrl, UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps) continue;
                string key = PreviewKey(theme);
                if (queuedPreviews.Contains(key)) continue;
                bool cached = false;
                try { var found = ImageLibrary.Call("HasImage", key, (ulong)0); cached = found is bool && (bool)found; }
                catch (Exception ex) { PrintWarning("Image Library indisponível: " + ex.Message); }
                if (cached) continue;
                queuedPreviews.Add(key);
                try
                {
                    ImageLibrary.Call("AddImage", theme.PreviewImageUrl, key, (ulong)0, new Action(() =>
                    {
                        foreach (var player in BasePlayer.activePlayerList)
                            if (player != null && player.IsConnected && selectedGrade.ContainsKey(player.userID)) Draw(player);
                    }));
                }
                catch (Exception ex)
                {
                    queuedPreviews.Remove(key);
                    PrintWarning("Falha ao registrar imagem de " + theme.Name + ": " + ex.Message);
                }
            }
        }
        private string PreviewKey(Theme theme)
        {
            return !String.IsNullOrWhiteSpace(theme.PreviewImageKey)
                ? theme.PreviewImageKey
                : "nunusbuild_" + theme.Grade.ToLowerInvariant() + "_" + theme.SkinId;
        }
        private void SaveData() { Interface.Oxide.DataFileSystem.WriteObject(Name, saved); }
        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList) CuiHelper.DestroyUi(player, Root);
            SaveData();
        }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null) { selectedGrade.Remove(player.userID); page.Remove(player.userID); }
        }
        private bool IsAdmin(BasePlayer player) => player != null && permission.UserHasPermission(player.UserIDString, PermAdmin);
        private bool CanUse(BasePlayer player) => player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, PermUse);
        private string GradeName(string g) => g == "Wood" ? "MADEIRA" : g == "Stone" ? "PEDRA" : g == "Metal" ? "METAL" : "BLINDADO";
        private string GradeFromEnum(BuildingGrade.Enum grade)
        {
            if (grade == BuildingGrade.Enum.Wood) return "Wood";
            if (grade == BuildingGrade.Enum.Stone) return "Stone";
            if (grade == BuildingGrade.Enum.Metal) return "Metal";
            if (grade == BuildingGrade.Enum.TopTier) return "TopTier";
            return null;
        }
        private ulong Preference(ulong user, string grade)
        {
            Dictionary<string, ulong> items;
            ulong value;
            return saved.Preferences.TryGetValue(user, out items) && items.TryGetValue(grade, out value) ? value : 0;
        }
        private void SetPreference(ulong user, string grade, ulong id)
        {
            Dictionary<string, ulong> items;
            if (!saved.Preferences.TryGetValue(user, out items)) { items = new Dictionary<string, ulong>(); saved.Preferences[user] = items; }
            items[grade] = id;
            SaveData();
        }
        private bool IsListed(string grade, ulong id) => settings.Themes.Any(x => x.Grade == grade && x.SkinId == id);

        [ChatCommand("nunusbuild")]
        private void Open(BasePlayer player, string command, string[] args)
        {
            if (!CanUse(player)) { SendReply(player, "Sem permissão: nunusbuildingthemes.use"); return; }
            selectedGrade[player.userID] = "Stone";
            page[player.userID] = 0;
            Draw(player);
        }
        [ConsoleCommand("nunusbuild.action")]
        private void Action(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (!CanUse(player) || arg.Args == null || arg.Args.Length < 1) return;
            string action = arg.Args[0].ToString();
            if (action == "close") { CuiHelper.DestroyUi(player, Root); return; }
            string grade;
            if (!selectedGrade.TryGetValue(player.userID, out grade)) return;
            if (action == "grade" && arg.Args.Length == 2)
            {
                string requested = arg.Args[1].ToString();
                if (requested != "Wood" && requested != "Stone" && requested != "Metal" && requested != "TopTier") return;
                selectedGrade[player.userID] = requested;
                page[player.userID] = 0;
            }
            else if (action == "page" && arg.Args.Length == 2)
            {
                int delta;
                if (!int.TryParse(arg.Args[1].ToString(), out delta) || (delta != 1 && delta != -1)) return;
                page[player.userID] = Math.Max(0, page[player.userID] + delta);
            }
            else if (action == "pick" && arg.Args.Length == 2)
            {
                int index;
                if (!int.TryParse(arg.Args[1].ToString(), out index) || index < 0) return;
                var options = settings.Themes.Where(t => t.Grade == grade && (t.SkinId != 0 || t.Name == "Padrão")).ToList();
                if (index >= options.Count) return;
                if (options[index].SkinId == 0 && options[index].Name != "Padrão")
                {
                    SendReply(player, "Tema sem SkinId verificado. Selecione um tema disponível.");
                    return;
                }
                SetPreference(player.userID, grade, options[index].SkinId);
                SendReply(player, "Tema selecionado: " + options[index].Name + ". Aplicação automática apenas durante teste administrativo habilitado.");
            }
            Draw(player);
        }
        [ConsoleCommand("nunusbuild.test")]
        private void Test(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !IsAdmin(player)) return;
            if (!settings.EnableTemporaryTestApplication) { SendReply(player, "Teste desativado no config. Ative explicitamente apenas em servidor de testes."); return; }
            if (arg.Args == null || arg.Args.Length != 1) return;
            int minutes;
            if (!int.TryParse(arg.Args[0].ToString(), out minutes)) return;
            if (minutes <= 0) { testingUntilUtc = DateTime.MinValue; SendReply(player, "Aplicação de teste desligada."); return; }
            minutes = Math.Min(minutes, settings.MaximumTestMinutes);
            testingUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            SendReply(player, "Teste temporário ativado por " + minutes + " minutos. Apenas upgrades feitos por administradores serão afetados.");
        }
        // After a successful, paid in-game upgrade, only for an explicitly enabled,
        // temporary test and only when the player has administrative permission.
        private void OnStructureUpgraded(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong originalSkin)
        {
            if (block == null || player == null || !IsAdmin(player) || !settings.EnableTemporaryTestApplication || DateTime.UtcNow >= testingUntilUtc) return;
            string key = GradeFromEnum(grade);
            if (key == null) return;
            ulong skin = Preference(player.userID, key);
            if (skin == 0 || !IsListed(key, skin)) return;
            block.skinID = skin;
            block.UpdateSkin();
            block.SendNetworkUpdate();
        }
        private string Coord(float n) => n.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        private string Rect(float a, float b) => Coord(a) + " " + Coord(b);
        private void Label(CuiElementContainer c, string parent, string value, string min, string max, int size = 14)
        {
            c.Add(new CuiLabel { Text = { Text = value, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "0.95 0.97 1 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Button(CuiElementContainer c, string parent, string value, string command, string min, string max, string color = "0.17 0.27 0.37 1")
        {
            c.Add(new CuiButton { Button = { Color = color, Command = command }, Text = { Text = value, FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent);
        }
        private void Draw(BasePlayer player)
        {
            if (!CanUse(player)) return;
            string grade;
            if (!selectedGrade.TryGetValue(player.userID, out grade)) return;
            CuiHelper.DestroyUi(player, Root);
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.06 0.09 0.13 0.98" }, RectTransform = { AnchorMin = "0.13 0.13", AnchorMax = "0.87 0.87" }, CursorEnabled = true }, "Overlay", Root);
            Label(c, Root, "NUNUS BUILD  |  TEMAS DE CONSTRUÇÃO", "0.04 0.92", "0.87 0.99", 20);
            Button(c, Root, "X", "nunusbuild.action close", "0.93 0.93", "0.985 0.985", "0.6 0.15 0.17 1");
            var grades = new[] { "Wood", "Stone", "Metal", "TopTier" };
            for (int i = 0; i < grades.Length; i++)
            {
                float x = 0.035f + i * 0.244f;
                Button(c, Root, GradeName(grades[i]), "nunusbuild.action grade " + grades[i], Rect(x, 0.84f), Rect(x + 0.228f, 0.905f), grade == grades[i] ? "0.19 0.42 0.62 1" : "0.17 0.24 0.31 1");
            }
            var options = settings.Themes.Where(t => t.Grade == grade && (t.SkinId != 0 || t.Name == "Padrão")).ToList();
            int pages = Math.Max(1, (options.Count + 5) / 6);
            int current = Math.Min(Math.Max(0, page.ContainsKey(player.userID) ? page[player.userID] : 0), pages - 1);
            page[player.userID] = current;
            ulong currentChoice = Preference(player.userID, grade);
            for (int i = 0; i < 6; i++)
            {
                int index = current * 6 + i;
                if (index >= options.Count) break;
                Theme theme = options[index];
                int col = i % 3, row = i / 3;
                float x = 0.035f + col * 0.326f;
                float top = 0.79f - row * 0.34f;
                string tile = c.Add(new CuiPanel { Image = { Color = "0.12 0.17 0.23 1" }, RectTransform = { AnchorMin = Rect(x, top - 0.32f), AnchorMax = Rect(x + 0.31f, top) } }, Root);
                string image = null;
                if (ImageLibrary != null)
                {
                    try
                    {
                        string key = PreviewKey(theme);
                        var found = ImageLibrary.Call("HasImage", key, (ulong)0);
                        if (found is bool && (bool)found)
                            image = ImageLibrary.Call("GetImage", key, (ulong)0) as string;
                    }
                    catch (Exception ex) { PrintWarning("Erro consultando prévia: " + ex.Message); }
                }
                if (!String.IsNullOrEmpty(image))
                    c.Add(new CuiElement { Parent = tile, Components = { new CuiRawImageComponent { Png = image, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0.2 0.27", AnchorMax = "0.8 0.95" } } });
                else Label(c, tile, String.IsNullOrWhiteSpace(theme.PreviewImageUrl) ? "Prévia não configurada" : "Carregando prévia...", "0.1 0.31", "0.9 0.88", 12);
                Label(c, tile, theme.Name + (currentChoice == theme.SkinId && (theme.SkinId != 0 || theme.Name == "Padrão") ? "  [SELECIONADO]" : ""), "0.03 0.16", "0.97 0.30", 12);
                Button(c, tile, "SELECIONAR", "nunusbuild.action pick " + index, "0.12 0.02", "0.88 0.16", "0.15 0.42 0.27 1");
            }
            Button(c, Root, "ANTERIOR", "nunusbuild.action page -1", "0.28 0.04", "0.44 0.095");
            Label(c, Root, "Página " + (current + 1) + "/" + pages, "0.44 0.04", "0.56 0.095");
            Button(c, Root, "PRÓXIMA", "nunusbuild.action page 1", "0.56 0.04", "0.72 0.095");
            CuiHelper.AddUi(player, c);
        }
    }
}
