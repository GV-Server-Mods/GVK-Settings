using Sandbox.ModAPI;

namespace GVK.Navigation
{
    /// <summary>
    /// The satellite map as a game screen (MyModScreen, SE 1.211). While it is open it takes the mouse and
    /// keyboard, so the player's vehicle coasts, and it closes on Esc or the map key.
    /// GVK_ZoneNavCommand does the layout, input and drawing; this class only forwards the screen's callbacks.
    /// </summary>
    public class GvkMapScreen : MyModScreen
    {
        private readonly GVK_ZoneNavCommand _owner;

        public GvkMapScreen(GVK_ZoneNavCommand owner) : base("GVK Satellite Map")
        {
            _owner = owner;
        }

        protected override void OnHandleInput()
        {
            _owner.HandleMapScreenInput();
        }

        protected override bool OnCancelling()
        {
            return _owner.OnMapScreenCancelling();
        }

        protected override void OnDraw(float transitionAlpha)
        {
            _owner.DrawMapScreen(transitionAlpha);
        }

        protected override void OnClosed(bool isUnloading)
        {
            _owner.OnMapScreenClosed(isUnloading);
        }
    }
}
