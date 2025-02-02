using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using ShopAPI;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace ShopFOV
{
    public class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("items")]
        public Dictionary<string, ShopItem> Items { get; set; } = [];
    }

    public class ShopItem
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("price")]
        public int Price { get; set; }

        [JsonPropertyName("sell_price")]
        public int SellPrice { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }

        [JsonPropertyName("fov")]
        public int FOV { get; set; } = 90;
    }

    [MinimumApiVersion(80)]
    public class ShopFOV : BasePlugin, IPluginConfig<PluginConfig>
    {
        public override string ModuleName => "[SHOP] FOV";
        public override string ModuleDescription => "Provides FOV customization through shop";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v2.0.0";

        public PluginConfig Config { get; set; } = null!;
        private IShopApi? _shopApi;
        private readonly ConcurrentDictionary<int, PlayerFOV> _playerFovs = new();
        private const string CategoryName = "FOV";

        public void OnConfigParsed(PluginConfig config)
        {
            Config = config;
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _shopApi = IShopApi.Capability.Get();
            if (_shopApi == null)
            {
                Logger.LogError("Failed to get Shop API capability!");
                return;
            }

            InitializeShopCategory();
            RegisterEventHandlers();
        }

        private void InitializeShopCategory()
        {
            if (Config.Items.Count == 0)
            {
                Logger.LogWarning("No items found in configuration!");
                return;
            }

            _shopApi!.CreateCategory(CategoryName, Localizer["CategoryName"]);

            foreach (var (itemId, item) in Config.Items.OrderBy(x => x.Value.FOV))
            {
                try
                {
                    var shopItemId = _shopApi.AddItem(
                        itemId,
                        item.Name,
                        CategoryName,
                        item.Price,
                        item.SellPrice,
                        item.Duration
                    ).GetAwaiter().GetResult();

                    _shopApi.SetItemCallbacks(
                        shopItemId,
                        OnClientBuyItem,
                        OnClientSellItem,
                        OnClientToggleItem
                    );
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to initialize item {itemId}: {ex.Message}");
                }
            }
        }

        private void RegisterEventHandlers()
        {
            RegisterListener<Listeners.OnClientDisconnect>(slot =>
            {
                _ = _playerFovs.TryRemove(slot, out _);
            });
        }

        private HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string category, string itemName, int price, int sellPrice, int duration, int count)
        {
            if (!IsValidPlayer(player) || !Config.Items.TryGetValue(itemName, out var item))
                return HookResult.Continue;

            _playerFovs[player.Slot] = new PlayerFOV(item.FOV, itemId);
            ApplyFov(player);
            return HookResult.Continue;
        }

        private HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string itemName, int state)
        {
            if (!IsValidPlayer(player))
                return HookResult.Continue;

            _ = state == 1
                ? OnClientBuyItem(player, itemId, CategoryName, itemName, 0, 0, 0, 0)
                : OnClientSellItem(player, itemId, itemName, 0);
            return HookResult.Continue;
        }

        private HookResult OnClientSellItem(CCSPlayerController player, int itemId, string itemName, int sellPrice)
        {
            if (!IsValidPlayer(player))
                return HookResult.Continue;

            _ = _playerFovs.TryRemove(player.Slot, out _);
            ResetFov(player);
            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (IsValidPlayer(player!))
            {
                ApplyFov(player!);
            }
            return HookResult.Continue;
        }

        private void ApplyFov(CCSPlayerController player)
        {
            if (_playerFovs.TryGetValue(player.Slot, out var fovData))
            {
                player.DesiredFOV = (uint)fovData.FOV;
                Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            }
        }

        private static void ResetFov(CCSPlayerController player)
        {
            player.DesiredFOV = 90;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
        }

        private static bool IsValidPlayer(CCSPlayerController player)
        {
            return player is { IsValid: true, PlayerPawn.IsValid: true, IsBot: false };
        }

        private record PlayerFOV(int FOV, int ItemId);
    }
}
