using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.Common
{
    public sealed class UIBackgroundView : UIView<UIBackgroundViewModel>
    {
        protected override void BindViewModel()
        {
            Root.pickingMode = PickingMode.Ignore;
        }
    }
}
