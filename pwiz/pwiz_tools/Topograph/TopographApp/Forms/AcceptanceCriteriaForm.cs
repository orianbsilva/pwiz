﻿/*
 * Original author: Nicholas Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2011 University of Washington - Seattle, WA
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using pwiz.Topograph.Model;
using pwiz.Topograph.Data;

namespace pwiz.Topograph.ui.Forms
{
    public partial class AcceptanceCriteriaForm : WorkspaceForm
    {
        public AcceptanceCriteriaForm(Workspace workspace) : base(workspace)
        {
            InitializeComponent();
            checkedListBoxAcceptableIntegrationNotes.Items.AddRange(IntegrationNote.Values().ToArray());
            AcceptSamplesWithoutMs2Id = workspace.GetAcceptSamplesWithoutMs2Id();
            MinDeconvolutionScore = workspace.GetAcceptMinDeconvolutionScore();
            MinAuc = workspace.GetAcceptMinAreaUnderChromatogramCurve();
            IntegrationNotes = workspace.GetAcceptIntegrationNotes();
        }

        public void Save()
        {
            using (Workspace.GetWriteLock())
            {
                Workspace.SetAcceptSamplesWithoutMs2Id(AcceptSamplesWithoutMs2Id);
                Workspace.SetAcceptMinDeconvolutionScore(MinDeconvolutionScore);
                Workspace.SetAcceptMinAreaUnderChromatogramCurve(MinAuc);
                Workspace.SetAcceptIntegrationNotes(IntegrationNotes);
            }
        }

        public bool AcceptSamplesWithoutMs2Id
        {
            get { return cbxAllowNoMs2Id.Checked; }
            set { cbxAllowNoMs2Id.Checked = value;}
        }

        public double MinDeconvolutionScore
        {
            get
            {
                double result;
                if (Double.TryParse(tbxMinDeconvolutionScore.Text, out result))
                {
                    return result;
                }
                return 0;
            }
            set
            {
                tbxMinDeconvolutionScore.Text = value.ToString();
            }
        }

        public double MinAuc
        {
            get { double result;
            if (Double.TryParse(tbxMinAuc.Text, out result))
            {
                return result;
            }
                return 0;
            }
            set
            {
                tbxMinAuc.Text = value.ToString();
            }
        }

        public IEnumerable<IntegrationNote> IntegrationNotes
        {
            get
            {
                return checkedListBoxAcceptableIntegrationNotes.CheckedItems.Cast<IntegrationNote>();
            }
            set
            {
                var set = new HashSet<IntegrationNote>(value);
                for (int i = 0; i < checkedListBoxAcceptableIntegrationNotes.Items.Count; i++)
                {
                    var item = (IntegrationNote) checkedListBoxAcceptableIntegrationNotes.Items[i];
                    checkedListBoxAcceptableIntegrationNotes.SetItemChecked(i, set.Contains(item));
                }
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            Save();
            Close();
        } 
    }
}