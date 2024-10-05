using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ManaRework;

public class ManaRework : Mod {

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