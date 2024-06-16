using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopFOV
{
    public class ShopFOV : BasePlugin
    {
        public override string ModuleName => "[SHOP] FOV";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IShopApi? SHOP_API;
        private const string CategoryName = "FOV";
        public static JObject? JsonFOV { get; private set; }
        private readonly PlayerFOV[] playerFOV = new PlayerFOV[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/FOV.json");
            if (File.Exists(configPath))
            {
                JsonFOV = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonFOV == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Óדמכ מבחמנא");

            var sortedItems = JsonFOV
                .Properties()
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .OrderBy(p => (int)p.Value["fov"]!)
                .ToList();

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(item.Key, (string)item.Value["name"]!, CategoryName, (int)item.Value["price"]!, (int)item.Value["sellprice"]!, (int)item.Value["duration"]!);
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerFOV[playerSlot] = null!);
        }

        public void OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetItemFOV(uniqueName, out int fov))
            {
                playerFOV[player.Slot] = new PlayerFOV(fov, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'fov' in config!");
            }
        }

        public void OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetItemFOV(uniqueName, out int fov))
            {
                playerFOV[player.Slot] = new PlayerFOV(fov, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
        }

        public void OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerFOV[player.Slot] = null!;
            player.DesiredFOV = 90;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && !player.IsBot && playerFOV[player.Slot] != null)
            {
                ChangeFov(player);
            }
            return HookResult.Continue;
        }

        private void ChangeFov(CCSPlayerController player)
        {
            var fov = (uint)playerFOV[player.Slot].FOV;
            player.DesiredFOV = fov;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
        }

        private bool TryGetItemFOV(string uniqueName, out int fov)
        {
            fov = 90;
            if (JsonFOV != null && JsonFOV.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["fov"] != null && jsonItem["fov"]!.Type != JTokenType.Null)
            {
                fov = (int)jsonItem["fov"]!;
                return true;
            }
            return false;
        }

        public record class PlayerFOV(int FOV, int ItemID);

    }
}