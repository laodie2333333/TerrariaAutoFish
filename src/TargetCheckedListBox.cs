using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace TerrariaAutoFish
{
    // Native CheckedListBox also toggles a repeated click on the selected row. Keep
    // native selection/scrolling, but accept mouse checks only on the drawn glyph.
    internal sealed class TargetCheckedListBox : CheckedListBox
    {
        private const int WmMouseFirst = 0x0200;
        private const int WmMouseLast = 0x020E;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmLeftButtonDoubleClick = 0x0203;
        private bool handlingMouseMessage;
        private bool mouseCanCheck;
        private bool vetoNextNativeCheck;
        private int mouseCheckIndex = -1;

        [DllImport("user32.dll")]
        private static extern int GetScrollPos(IntPtr window, int bar);

        public TargetCheckedListBox() { CheckOnClick = true; }

        internal Rectangle CheckBoxBounds(int index)
        {
            if (index < 0 || index >= Items.Count) return Rectangle.Empty;
            Rectangle row = GetItemRectangle(index);
            int glyphSize = 13; // CheckedListBox's classic, non-themed glyph size.
            if (Application.RenderWithVisualStyles)
            {
                using (Graphics graphics = CreateGraphics())
                    glyphSize = CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal).Width;
            }
            // CheckedListBox uses the same DPI-scaled inset on each side of its
            // row height. Derive it from public metrics rather than a fixed pixel.
            int inset = Math.Max(1, (ItemHeight - Font.Height) / 2);
            int top = Math.Max((ItemHeight - glyphSize) / 2, 0);
            if (top + glyphSize > row.Height) top = row.Height - glyphSize;
            int left = RightToLeft == RightToLeft.Yes
                ? row.Right - glyphSize - inset : row.Left + inset;
            // LB_GETITEMRECT does not include the native drawing DC's horizontal
            // window origin. A scrolled-off checkbox must not hit text at x = 0.
            if (HorizontalScrollbar && IsHandleCreated) left -= GetScrollPos(Handle, 0);
            return Rectangle.Intersect(new Rectangle(left, row.Top + top, glyphSize, glyphSize), ClientRectangle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0215 || m.Msg == 0x001F) mouseCheckIndex = -1; // CaptureChanged / CancelMode.
            if (m.Msg < WmMouseFirst || m.Msg > WmMouseLast)
            {
                base.WndProc(ref m);
                return;
            }
            bool previousHandling = handlingMouseMessage, previousCanCheck = mouseCanCheck;
            handlingMouseMessage = true;
            mouseCanCheck = false;
            if (m.Msg == WmLeftButtonDown || m.Msg == WmLeftButtonDoubleClick || m.Msg == WmLeftButtonUp)
            {
                long coordinates = m.LParam.ToInt64();
                var point = new Point(unchecked((short)(coordinates & 0xFFFF)),
                    unchecked((short)((coordinates >> 16) & 0xFFFF)));
                int index = ClientRectangle.Contains(point) ? IndexFromPoint(point) : -1;
                bool onGlyph = index >= 0 && CheckBoxBounds(index).Contains(point);
                if (m.Msg != WmLeftButtonUp) mouseCheckIndex = onGlyph ? index : -1;
                mouseCanCheck = onGlyph && mouseCheckIndex == index;
            }
            try { base.WndProc(ref m); }
            finally
            {
                handlingMouseMessage = previousHandling;
                mouseCanCheck = previousCanCheck;
                if (m.Msg == WmLeftButtonUp) mouseCheckIndex = -1;
            }
        }

        protected override void WmReflectCommand(ref Message m)
        {
            int notification = (int)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            if (!handlingMouseMessage || (notification != 1 && notification != 2))
            {
                base.WmReflectCommand(ref m);
                return;
            }
            bool previousVeto = vetoNextNativeCheck, previousCheckOnClick = CheckOnClick;
            vetoNextNativeCheck = !mouseCanCheck;
            // Ensure the native selection notification raises exactly one check
            // attempt, including a first click. Consume only that attempt so an
            // application SetItemChecked from SelectionChanged still works.
            CheckOnClick = true;
            try { base.WmReflectCommand(ref m); }
            finally
            {
                vetoNextNativeCheck = previousVeto;
                CheckOnClick = previousCheckOnClick;
            }
        }

        protected override void OnItemCheck(ItemCheckEventArgs e)
        {
            if (vetoNextNativeCheck)
            {
                vetoNextNativeCheck = false;
                e.NewValue = e.CurrentValue;
                return;
            }
            if (handlingMouseMessage) mouseCheckIndex = -1;
            base.OnItemCheck(e);
        }
    }
}
