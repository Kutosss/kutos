using Content.Client.Changelog;
using Content.Client.Credits;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client.Info
{
    public sealed class ServerInfo : BoxContainer
    {
        private readonly RichTextLabel _richTextLabel;

        public ServerInfo()
        {
            Orientation = LayoutOrientation.Vertical;

            _richTextLabel = new RichTextLabel
            {
                VerticalExpand = true
            };
            AddChild(_richTextLabel);
        }
        public void SetInfoBlob(string markup)
        {
            // WD EDIT START
            var message = FormattedMessage.FromMarkupOrThrow("[font=\"Bedstead\" size=12][color=#2f2f2f]" + markup + "[/color][/font]");
            _richTextLabel.SetMessage(message);
            // WD EDIT END

        }
    }
}
