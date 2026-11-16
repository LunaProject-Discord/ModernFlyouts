using ModernFlyouts.Controls;
using ModernFlyouts.Core.Interop;
using ModernFlyouts.Helpers;

namespace ModernFlyouts
{
    public class VirtualDesktopFlyoutHelper : FlyoutHelperBase
    {
        private VirtualDesktopControl virtualDesktopControl;

        public VirtualDesktopFlyoutHelper()
        {
            Initialize();
        }

        public void Initialize()
        {
            AlwaysHandleDefaultFlyout = true;

            virtualDesktopControl = new VirtualDesktopControl();

            PrimaryContent = virtualDesktopControl;

            OnEnabled();
        }

        public override bool CanHandleNativeOnScreenFlyout(FlyoutTriggerData triggerData)
        {
            if (triggerData.TriggerType == FlyoutTriggerType.VirtualDesktop
                && triggerData.Data is VirtualDesktopInfo desktop)
            {
                string name = string.IsNullOrWhiteSpace(desktop.Name) && desktop.Index >= 0
                    ? string.Format(Properties.Strings.VirtualDesktopFlyoutHelper_DefaultName, desktop.Index + 1)
                    : desktop.Name;

                virtualDesktopControl.Update(name, desktop.Index, desktop.Count);
                return true;
            }

            return base.CanHandleNativeOnScreenFlyout(triggerData);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            AppDataHelper.VirtualDesktopModuleEnabled = IsEnabled;
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            AppDataHelper.VirtualDesktopModuleEnabled = IsEnabled;
        }
    }
}
