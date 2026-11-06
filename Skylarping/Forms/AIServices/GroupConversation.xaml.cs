/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System.Windows.Controls;
using Skylarping.Preferences;

namespace Skylarping.Forms.AIServicePages
{
    public partial class GroupConversation : Page
    {
        public GroupConversation()
        {
            InitializeComponent();
            DirectQuestionsBox.IsChecked = Settings.GroupDirectQuestions;
        }

        public void Commit()
        {
            Settings.GroupDirectQuestions = DirectQuestionsBox.IsChecked == true;
        }
    }
}
