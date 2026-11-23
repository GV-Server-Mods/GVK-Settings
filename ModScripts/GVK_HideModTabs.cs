using Sandbox.Definitions;
using VRage.Game.Components;

namespace GVK.HideModTabs
{
    // Hides other mods' G menu tabs whose blocks are listed in the GVK tabs (Docs/design/g-menu.md).
    // Tabs with one Name merge and the first loaded keeps its flags, so this can't be done in data.
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class GVK_HideModTabs : MySessionComponentBase
    {
        static readonly string[] Hidden = { "AutoMcD_Astronautical", "Spawn_Point_IR", "Blockers", "GV_", "XLBlocks" };

        public override void LoadData()
        {
            var categories = MyDefinitionManager.Static.GetCategories();
            foreach (var name in Hidden)
            {
                MyGuiBlockCategoryDefinition category;
                if (categories.TryGetValue(name, out category))
                    category.IsBlockCategory = false;
            }
        }
    }
}
