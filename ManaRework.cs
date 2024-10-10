using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.ComponentModel;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.IO;

namespace ManaRework;

public class ManaRework : Mod {
    public Hook h;
    private static readonly FieldInfo consumedManaCrystals = typeof(Player).GetField("consumedManaCrystals", BindingFlags.NonPublic | BindingFlags.Instance);

    public override void Load() {
        IL_Player.ItemCheck_UseManaCrystal += ItemCheck_UseManaCrystal;
        h = new(typeof(Player).GetMethod("set_ConsumedManaCrystals", BindingFlags.Public | BindingFlags.Instance), OnSetConsumedManaCrystals);
        h.Apply();
    }

    public override void Unload() {
        IL_Player.ItemCheck_UseManaCrystal -= ItemCheck_UseManaCrystal;
        h?.Undo();
    }

    private static void OnSetConsumedManaCrystals(Action<Player, int> orig, Player player, int val) {
        if (ManaReworkConfig.Instance.UnlimitedManaCrystals)
            consumedManaCrystals.SetValue(player, val);
        else if (ManaReworkConfig.Instance.ZeroBaseMana)
            consumedManaCrystals.SetValue(player, Math.Clamp(val, 0, Player.ManaCrystalMax + 1));
        else
            orig(player, val);
        if (ManaReworkConfig.Instance.UnlimitedManaCrystals && val > Player.ManaCrystalMax || ManaReworkConfig.Instance.ZeroBaseMana && val == Player.ManaCrystalMax + 1)
            player.GetModPlayer<ManaReworkPlayer>().ActuallyConsumedManaCrystals = val;
    }

    private void ItemCheck_UseManaCrystal(ILContext il) {
        try {
            ILCursor c = new(il);
            c.GotoNext(MoveType.After, i => i.MatchLdcI4(Player.ManaCrystalMax));
            ILLabel vanilla = il.DefineLabel();
            c.Emit(OpCodes.Call, typeof(ManaReworkConfig).GetMethod("get_Instance", BindingFlags.Public | BindingFlags.Static));
            c.Emit(OpCodes.Call, typeof(ManaReworkConfig).GetMethod("get_UnlimitedManaCrystals", BindingFlags.Public | BindingFlags.Instance));
            ILLabel zeroBaseMana = il.DefineLabel();
            c.Emit(OpCodes.Brfalse_S, zeroBaseMana);
            c.Emit(OpCodes.Pop);
            c.Emit(OpCodes.Ldc_I4, int.MaxValue / 21); // divided by 20 so that the player's mana value doesn't overflow
            // divided by 21 so that modded mana additions don't cause the player's mana to overflow
            c.Emit(OpCodes.Br_S, vanilla);
            c.MarkLabel(zeroBaseMana);
            c.Emit(OpCodes.Call, typeof(ManaReworkConfig).GetMethod("get_Instance", BindingFlags.Public | BindingFlags.Static));
            c.Emit(OpCodes.Call, typeof(ManaReworkConfig).GetMethod("get_ZeroBaseMana", BindingFlags.Public | BindingFlags.Instance));
            c.Emit(OpCodes.Brfalse_S, vanilla);
            c.Emit(OpCodes.Pop); // remove mana crystal max from the stack
            c.Emit(OpCodes.Ldc_I4, Player.ManaCrystalMax + 1);
            c.MarkLabel(vanilla);
        } catch (Exception e) {
            MonoModHooks.DumpIL(this, il);
            throw new ILPatchFailureException(this, il, e);
        }
    }
}

public class ManaReworkConfig : ModConfig {
    public static ManaReworkConfig Instance { get; private set; }
    
    public override ConfigScope Mode => ConfigScope.ServerSide;

    [DefaultValue(false)]
    public bool UnlimitedManaCrystals { get; set; }

    [DefaultValue(true)]
    public bool ZeroBaseMana { get; set; }

    [DefaultValue(true)]
    [ReloadRequired]
    public bool MoreExpensiveManaCrystals { get; set; }

    [DefaultValue(true)]
    [ReloadRequired]
    public bool MoreManaUsingItems { get; set; }

    public override void OnLoaded() => Instance = this;
}

public class ManaReworkPlayer : ModPlayer {
    public int ActuallyConsumedManaCrystals { get; set; }

    public override void ModifyMaxStats(out StatModifier health, out StatModifier mana) {
        health = new();
        mana = new();
        if (ManaReworkConfig.Instance.ZeroBaseMana)
            mana.Base = -20;
    }

    public override void SaveData(TagCompound tag) {
        tag["ConsumedManaCrystals"] = ActuallyConsumedManaCrystals;
    }

    public override void LoadData(TagCompound tag) {
        if (tag.TryGet("ConsumedManaCrystals", out int crystals))
            Player.ConsumedManaCrystals = ActuallyConsumedManaCrystals = crystals;
    }
}

public class ManaReworkSystem : ModSystem {
    public override void PostAddRecipes() {
        if (ManaReworkConfig.Instance.MoreExpensiveManaCrystals) {
            for (int i = 0; i < Recipe.numRecipes; i++) {
                Recipe recipe = Main.recipe[i];
                if (recipe.TryGetResult(ItemID.ManaCrystal, out _))
                    foreach (Item ingredient in recipe.requiredItem)
                        ingredient.stack *= 5;
            }
        }
    }
}

public class ManaReworkItem : GlobalItem {
    public override void SetDefaults(Item item) {
        if (ManaReworkConfig.Instance.MoreManaUsingItems) {
            switch (item.type) {
                case ItemID.CellPhone:
                case ItemID.Shellphone:
                case ItemID.ShellphoneDummy:
                case ItemID.ShellphoneHell:
                case ItemID.ShellphoneOcean:
                case ItemID.ShellphoneSpawn:
                case ItemID.IceMirror:
                case ItemID.MagicMirror: item.mana = 17; break;
                case ItemID.DemonConch:
                case ItemID.MagicConch: item.mana = 10; break;
                default: break;
            }

            if (Main.projPet[item.shoot])
                item.mana = 20;

            if (ModLoader.TryGetMod("CalamityMod", out Mod calamity))
                Compat(item, calamity);
            if (ModLoader.TryGetMod("THoriummod", out Mod thorium))
                Compat(item, thorium);
            if (ModLoader.TryGetMod("TheDepths", out Mod theDepths))
                Compat(item, theDepths,
                    ("ShalestoneConch", 10),
                    ("ShellPhoneDepths", 17));
        }
    }

    public static void Compat(Item item, Mod mod, params (string name, int mana)[] items) {
        foreach ((string name, int mana) in items)
            if (mod.TryFind(name, out ModItem modItem) && modItem.Type == item.type)
                item.mana = mana;
    }
}