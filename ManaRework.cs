using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

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
        consumedManaCrystals.SetValue(player, Math.Clamp(val, 0, Player.ManaCrystalMax + 1));
    }

    private void ItemCheck_UseManaCrystal(ILContext il) {
        try {
            ILCursor c = new(il);
            c.GotoNext(MoveType.After, i => i.MatchLdcI4(Player.ManaCrystalMax));
            c.Emit(OpCodes.Pop); // remove mana crystal max from the stack
            c.Emit(OpCodes.Ldc_I4, Player.ManaCrystalMax + 1);
        } catch (Exception e) {
            MonoModHooks.DumpIL(this, il);
            throw new ILPatchFailureException(this, il, e);
        }
    }
}

public class ManaReworkPlayer : ModPlayer {
    public override void ModifyMaxStats(out StatModifier health, out StatModifier mana) {
        health = new();
        mana = new() {
            Base = -20
        };
    }
}

public class ManaReworkSystem : ModSystem {
    public override void PostAddRecipes() {
        for (int i = 0; i < Recipe.numRecipes; i++) {
            Recipe recipe = Main.recipe[i];
            if (recipe.TryGetResult(ItemID.ManaCrystal, out _))
                foreach (Item ingredient in recipe.requiredItem)
                    ingredient.stack *= 5;
        }
    }
}

public class ManaReworkItem : GlobalItem {
    public override void SetDefaults(Item item) {
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

    public static void Compat(Item item, Mod mod, params (string name, int mana)[] items) {
        foreach ((string name, int mana) in items)
            if (mod.TryFind(name, out ModItem modItem) && modItem.Type == item.type)
                item.mana = mana;
    }
}