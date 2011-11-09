/*
 * Original author: Tahmina Baker <tabaker .at. u.washington.edu>,
 *                  UWPR, Department of Genome Sciences, UW
 *
 * Copyright 2010 University of Washington - Seattle, WA
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
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using pwiz.Common.SystemUtil;
using pwiz.MSGraph;
using pwiz.Skyline.Alerts;
using pwiz.Skyline.Controls;
using pwiz.Skyline.Controls.Graphs;
using pwiz.Skyline.Controls.SeqNode;
using pwiz.Skyline.EditUI;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.Lib;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.DocSettings.Extensions;
using ZedGraph;


namespace pwiz.Skyline.SettingsUI
{

    /// <summary>
    /// Interface for any window that contains a graph, to allow non-blocking
    /// updates with a <see cref="Timer"/>.
    /// </summary>
    public interface IGraphContainer : IUpdatable
    {
        /// <summary>
        /// Locks/unlocks the Y-axis, so that it auto-scales.
        /// </summary>
        /// <param name="lockY">True to use Y-axis auto-scaling</param>
        void LockYAxis(bool lockY);
    }

    /// <summary>
    /// Needed by the graph object.
    /// </summary>
    public interface IStateProvider
    {
        IList<IonType> ShowIonTypes { get; }
        IList<int> ShowIonCharges { get; }

        void BuildSpectrumMenu(ZedGraphControl zedGraphControl, ContextMenuStrip menuStrip);
    }

    /// <summary>
    /// Dialog to view the contents of the libraries in the Peptide Settings 
    /// dialog's Library tab. It allows you to select one of the libraries 
    /// from a drop-down, view and search the list of peptides, and view the
    /// spectrum for peptide selected in the list.
    /// </summary>
    public partial class ViewLibraryDlg : Form, IGraphContainer, IStateProvider, ITipDisplayer
    {
        // Used to parse the modification string in a given sequence
        private const string REGEX_MODIFICATION_PATTERN = @"\[[^\]]*\]";

        protected internal const int PADDING = 3;
        private const TextFormatFlags FORMAT_PLAIN = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        private const TextFormatFlags FORMAT_CUSTOM = FORMAT_PLAIN | TextFormatFlags.NoPadding;
        
        private readonly LibraryManager _libraryManager;
        private IList<LibrarySpec> _libraryListDoc;
        private IList<LibrarySpec> _libraryListThis;
        private string _selectedLibName;
        private Library _selectedLibrary;
        private LibrarySpec _selectedSpec;
        private Range _currentRange;
        private readonly PageInfo _pageInfo;
        private readonly IDocumentUIContainer _documentUiContainer;
        private SrmDocument Document { get { return _documentUiContainer.Document; }  }
        private readonly Bitmap _peptideImg;
        private MSGraphPane GraphPane { get { return (MSGraphPane)graphControl.MasterPane[0]; } }
        public ViewLibSpectrumGraphItem GraphItem { get; private set; }
        public GraphSpectrumSettings GraphSettings { get; private set; }
        public int LineWidth { get; set; }
        public float FontSize { get; set; }

        private ViewLibraryPepInfo[] _peptides;
        private byte[] _lookupPool;

        private bool _activated;

//        private int _listPeptidePrevSelIndex;

        private readonly NodeTip _nodeTip;
        private readonly MoveThreshold _moveThreshold = new MoveThreshold(5, 5);
        private ViewLibraryPepInfo? _lastTipNode;
        private ITipProvider _lastTipProvider;

        private ModFontHolder ModFonts { get; set; }

        private string SelectedLibraryName
        {
            get
            {
                if (!string.IsNullOrEmpty(_selectedLibName))
                    return _selectedLibName;
                if (comboLibrary.Items.Count > 0)
                    return _selectedLibName = (string)comboLibrary.Items[0];
                return null;
            }
        }

        public int PeptidesCount { get { return _peptides.Count(); } }

        public int PeptideDisplayCount { get { return listPeptide.Items.Count; } }

        /// <summary>
        /// Constructor for the View Library dialog.
        /// </summary>
        /// <param name="libMgr"> A library manager is needed to load the
        /// chosen peptide library. </param>
        /// <param name="libName"> Name of the library to select. </param>
        /// <param name="documentContainer"> The current document. </param>
        public ViewLibraryDlg(LibraryManager libMgr, String libName, IDocumentUIContainer documentContainer)
        {
            InitializeComponent();

            _libraryManager = libMgr;
            _selectedLibName = libName;
            _libraryListThis = _libraryListDoc = Settings.Default.SpectralLibraryList.ToList();
            _pageInfo = new PageInfo(100, 0, _currentRange);
            _documentUiContainer = documentContainer;
            _documentUiContainer.ListenUI(OnDocumentChange);

            graphControl.MasterPane.Border.IsVisible = false;
            var graphPane = GraphPane;
            graphPane.Border.IsVisible = false;
            graphPane.Title.IsVisible = true;
            graphPane.AllowCurveOverlap = true;
            GraphSettings = new GraphSpectrumSettings(UpdateUI);

            Icon = Resources.Skyline;
            ModFonts = new ModFontHolder(listPeptide);
            _peptideImg = Resources.PeptideLib;

            // Tip for peptides in list
            _nodeTip = new NodeTip(this);

            // Restore window placement.
            Point location = Settings.Default.ViewLibraryLocation;
            Size size = Settings.Default.ViewLibrarySize;

            if (!location.IsEmpty)
            {
                StartPosition = FormStartPosition.Manual;
                Location = location;
            }
            if (!size.IsEmpty)
                Size = size;

//            _listPeptidePrevSelIndex = -1;
        }

        private void graphControl_ContextMenuBuilder(ZedGraphControl sender, ContextMenuStrip menuStrip,
            Point mousePt, ZedGraphControl.ContextMenuObjectState objState)
        {
            BuildSpectrumMenu(sender, menuStrip);
        }

        /// <summary>
        /// Gets names of libraries in the Peptide Settings --> Library tab,
        /// displays them in the Library combobox, and selects the first one
        /// as the default library.
        /// </summary>
        private void InitializeLibrariesComboBox()
        {
            comboLibrary.BeginUpdate();

            comboLibrary.Items.Clear();

            foreach (var librarySpec in _libraryListThis)
                comboLibrary.Items.Add(librarySpec.Name);

            comboLibrary.SelectedItem = SelectedLibraryName;

            comboLibrary.EndUpdate();
        }

        /// <summary>
        /// Uses binary search to find the range of peptides in the list
        /// that match the string passed in. If none is found, a range of
        /// (-1, -1) is returned.
        /// </summary>
        private Range BinaryRangeSearch(string s, Range rangeIn)
        {
            // First try to find a single match for s within the peptides list
            var searchRange = new Range(rangeIn);
            if (s.Length > 0)
            {
                bool found = false;
                int mid = 0;
                while (searchRange.StartIndex <= searchRange.EndIndex)
                {
                    mid = (searchRange.StartIndex + searchRange.EndIndex) / 2;
                    int compResult = Compare(_peptides[mid], s);
                    if (compResult < 0)
                    {
                        searchRange.StartIndex = mid + 1;
                    }
                    else if (compResult > 0)
                    {
                        searchRange.EndIndex = mid - 1;
                    }
                    else
                    {
                        // We've found a single match
                        found = true;
                        break;
                    }
                }

                // We'll return (-1, -1) if nothing was found
                searchRange.StartIndex = -1;
                searchRange.EndIndex = -1;

                if (found)
                {
                    // Now that we've found a single match, we first need to
                    // walk up the array and find each matching peptide above
                    // it in the peptides list.
                    searchRange.StartIndex = mid;
                    int previousIndex = searchRange.StartIndex - 1;
                    while ((previousIndex >= rangeIn.StartIndex) &&
                            IsMatch(_peptides[previousIndex], s))
                    {
                        searchRange.StartIndex = previousIndex;
                        previousIndex--;
                    }

                    // Next, we walk down the array and find each matching 
                    // peptide below it in the peptides list.
                    searchRange.EndIndex = mid;
                    int nextIndex = searchRange.EndIndex + 1;
                    while ((nextIndex <= rangeIn.EndIndex) &&
                            IsMatch(_peptides[nextIndex], s))
                    {
                        searchRange.EndIndex = nextIndex;
                        nextIndex++;
                    }
                }
            }

            return searchRange;
        }

        public bool AssociateMatchingProteins
        {
            get { return cbAssociateProteins.Checked; }
            set { cbAssociateProteins.Checked = value; }
        }

        public void ChangeSelectedPeptide(string text)
        {
            foreach(ViewLibraryPepInfo pepInfo in listPeptide.Items)
            {
                if (pepInfo.DisplayString.Contains(text))
                {
                    listPeptide.SelectedItem = pepInfo;
                    return;
                }
            }
        }

        #region Dialog Events

        private void ViewLibraryDlg_Load(object sender, EventArgs e)
        {
            // The combobox is the control that tells us which peptide library
            // we need to load, so as soon as the dialog loads, we need to
            // populate it and set a default selection for the library.
            InitializeLibrariesComboBox();
            UpdateViewLibraryDlg();
        }

        private void ViewLibraryDlg_Shown(object sender, EventArgs e)
        {
            textPeptide.Focus();
        }

        private void ViewLibraryDlg_Activated(object sender, EventArgs e)
        {
            // Used to set correct highlight color for selected item in the listPeptide.
            _activated = true;
            if (listPeptide.SelectedIndex != -1)
                listPeptide.Invalidate(listPeptide.GetItemRectangle(listPeptide.SelectedIndex));

            // Check to see if the library list has changed.
            var newSpectralLibraryList = Settings.Default.SpectralLibraryList.ToList();
            if (!ArrayUtil.EqualsDeep(newSpectralLibraryList, _libraryListDoc))
            {
                _libraryListDoc = newSpectralLibraryList;
                // If the current library spec is no longer part of the document settings, 
                // ask if user wants to reload the explorer. Otherwise, simply update the LibrariesComboBox.
                if (!_libraryListDoc.Contains(_selectedSpec))
                {
                    Program.MainWindow.FocusDocument();
                    using (var reloadExplorerMsg =
                        new MultiButtonMsgDlg(
                            string.Format("The library {0} is no longer part of the document settings. Reload the library explorer?",
                            _selectedLibName),
                            "Yes", "No"))
                    {
                        var result = reloadExplorerMsg.ShowDialog(this);
                        if (result == DialogResult.Yes)
                        {
                            if (newSpectralLibraryList.Count == 0)
                            {
                                MessageDlg.Show(this, "There are no libraries in the current settings.");
                                Close();
                                return;
                            }
                            _libraryListThis = _libraryListDoc;
                            InitializeLibrariesComboBox();
                            comboLibrary.SelectedIndex = 0;
                            Activate();
                        }
                    }
                }
                else
                {
                    _libraryListThis = _libraryListDoc;
                    InitializeLibrariesComboBox();
                }
            }
        }

        private void ViewLibraryDlg_Deactivate(object sender, EventArgs e)
        {
            _activated = false;
            if (listPeptide.SelectedIndex != -1)
                listPeptide.Invalidate(listPeptide.GetItemRectangle(listPeptide.SelectedIndex));
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_selectedLibrary != null)
                _selectedLibrary.ReadStream.CloseStream();
            _documentUiContainer.UnlistenUI(OnDocumentChange);
            base.OnClosed(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Settings.Default.ViewLibraryLocation = Location;
            Settings.Default.ViewLibrarySize = Size;

            base.OnClosing(e);
        }

        #endregion

        #region Update ViewLibraryDlg

        private void UpdateViewLibraryDlg()
        {
            // Order matters!!
            LoadLibrary();
            InitializePeptides();
            _currentRange = BinaryRangeSearch(textPeptide.Text, new Range(0, _peptides.Length - 1));
            UpdatePageInfo();
            UpdateStatusArea();
            cbShowModMasses.Checked = Settings.Default.ShowModMassesInExplorer;
            UpdateListPeptide(0);
            textPeptide.Select();
            UpdateUI();
        }

        /// <summary>
        /// Loads the library selected in the Library combobox. First, we check
        /// to see if the library is already loaded in the main window. If yes,
        /// great, we can just use that. If no, we need to use the LongWaitDlg
        /// as the IProgressMonitor to load the library from the LibrarySpec.
        /// </summary>
        private void LoadLibrary()
        {
            LibrarySpec selectedLibrarySpec = _selectedSpec = _libraryListThis[comboLibrary.SelectedIndex];
            if (_selectedLibrary != null)
                _selectedLibrary.ReadStream.CloseStream();
            _selectedLibrary = _libraryManager.TryGetLibrary(selectedLibrarySpec);
            if (_selectedLibrary == null)
            {
                btnAddAll.Enabled = false;
                btnAdd.Enabled = false;
                var longWait = new LongWaitDlg { Text = "Loading Library" };
                try
                {
                    var status = longWait.PerformWork(this, 800, monitor =>
                    {
                        _selectedLibrary = selectedLibrarySpec.LoadLibrary(new ViewLibLoadMonitor(monitor));
                    });
                    if (status.IsError)
                    {
                        MessageBox.Show(this, status.ErrorException.Message, Program.Name);
                    }
                }
                catch (Exception x)
                {
                    MessageBox.Show(this, string.Format("An error occurred attempting to import the {0} library.\n{1}", selectedLibrarySpec.Name, x.Message), Program.Name);
                }
            }
            btnAddAll.Enabled = true;
            btnAdd.Enabled = true;
        }

        /// <summary>
        /// Loads the entire list of peptides from the library selected into an
        /// array of PeptideInfo objects and sorts them alphabetically, with a
        /// few small quirks related to the modification. Please see comments
        /// for the PeptideInfoComparer above.
        /// </summary>
        private void InitializePeptides()
        {
            var lookupPool = new List<byte>();
            _peptides = new ViewLibraryPepInfo[_selectedLibrary != null ? _selectedLibrary.Count : 0];
            if (_selectedLibrary != null)
            {
                int index = 0;
                foreach (var libKey in _selectedLibrary.Keys)
                {
                    var pepInfo = new ViewLibraryPepInfo(libKey, lookupPool);
                    _peptides[index] = pepInfo;
                    index++;
                }
                Array.Sort(_peptides, new PepInfoComparer(lookupPool));
            }

            _lookupPool = lookupPool.ToArray();
            _currentRange = new Range(0, _peptides.Length - 1);
        }

        /// <summary>
        /// Used to update page info when something changes: e.g. library
        /// selection changes, or search results change.
        /// </summary>
        private void UpdatePageInfo()
        {
            // We need to update the number of total items
            _pageInfo.ItemIndexRange = _currentRange;

            // Restart on page 1 (or 0 if we have no items)
            PreviousLink.Enabled = false;
            _pageInfo.Page = 0;
            if (_pageInfo.Items > 0)
            {
                _pageInfo.Page = 1;
            }

            // Enable the Next link if we have more than one page
            NextLink.Enabled = _pageInfo.Items > _pageInfo.PageSize;
        }

        /// <summary>
        /// Updates the status area showing which peptides are being shown.
        /// </summary>
        private void UpdateStatusArea()
        {
            int showStart = 0;
            int showEnd = 0;
            if (_currentRange.Count > 0)
            {
                showStart = _pageInfo.StartIndex + 1;
                showEnd = _pageInfo.EndIndex;
            }

            const string numFormat = "#,0";

            PeptideCount.Text = string.Format("Peptides {0} through {1} of {2} total.", 
                                                showStart.ToString(numFormat), 
                                                showEnd.ToString(numFormat), 
                                                _peptides.Length.ToString(numFormat));

            PageCount.Text = string.Format("Page {0} of {1}", _pageInfo.Page, _pageInfo.Pages);
        }

        /// <summary>
        /// Used to update the peptides list when something changes: e.g. 
        /// library selection changes, or search results change.
        /// </summary>
        private void UpdateListPeptide(int selectPeptideIndex)
        {
            var pepMatcher = new ViewLibraryPepMatching(Document,
                _selectedLibrary, _selectedSpec, _lookupPool, _peptides);
            listPeptide.BeginUpdate();
            listPeptide.Items.Clear();
            if (_currentRange.Count > 0)
            {
                int start = _pageInfo.StartIndex;
                int end = _pageInfo.EndIndex;
                for (int i = start; i < end; i++)
                {
                    ViewLibraryPepInfo pepInfo = _peptides[i];
                    int charge = pepInfo.Key.Charge;
                    var diff = new SrmSettingsDiff(true, false, true, false, false, false);
                    listPeptide.Items.Add(pepMatcher.AssociateMatchingPeptide(pepInfo, charge, diff));
                }

                listPeptide.SelectedIndex = selectPeptideIndex;
            }
            listPeptide.EndUpdate();
        }

        /// <summary>
        /// Updates the spectrum graph using the currently selected peptide.
        /// </summary>
        public void UpdateUI()
        {
            // Clear existing data from the graph pane
            var graphPane = (MSGraphPane)graphControl.MasterPane[0];
            graphPane.CurveList.Clear();
            graphPane.GraphObjList.Clear();

            labelRT.Text = "";
            labelFilename.Text = "";

            // Check for appropriate spectrum to load
            bool available = false;
            try
            {
                int index = GetIndexOfSelectedPeptide();
                if (-1 != index)
                {
                    SpectrumPeaksInfo spectrum;
                    if (_selectedLibrary.TryLoadSpectrum(_peptides[index].Key, out spectrum))
                    {
                        SrmSettings settings = Program.ActiveDocumentUI.Settings;
                        TransitionGroup transitionGroup;

                        var types = ShowIonTypes;
                        var charges = ShowIonCharges;
                        var rankTypes = settings.TransitionSettings.Filter.IonTypes;
                        var rankCharges = settings.TransitionSettings.Filter.ProductCharges;

                        ExplicitMods mods;
                        var pepInfo = (ViewLibraryPepInfo)listPeptide.SelectedItem;
                        var nodPep = pepInfo.PeptideNode;
                        if (nodPep != null)
                        {
                            mods = nodPep.ExplicitMods;
                            // Should always be just one child.  The child that matched this spectrum.
                            transitionGroup = ((TransitionGroupDocNode)nodPep.Children[0]).TransitionGroup;

                            var bestSpectrum = _selectedLibrary.GetSpectra(_peptides[index].Key,
                                IsotopeLabelType.light, true).FirstOrDefault();
                            if (bestSpectrum != null)
                            {
                                double? rt = bestSpectrum.RetentionTime;
                                string filename = bestSpectrum.FileName;
                                
                                if(!string.IsNullOrEmpty(filename))
                                {
                                    labelFilename.Text = string.Format("File: {0}", filename);
                                }
                                if(rt.HasValue)
                                {
                                    labelRT.Text = string.Format("RT: {0}", rt);
                                }
                            }
                        }
                        else
                        {
                            var peptide = new Peptide(null, _peptides[index].GetAASequence(_lookupPool),
                                                      null, null, 0);
                            transitionGroup = new TransitionGroup(peptide, _peptides[index].Charge,
                                                                  IsotopeLabelType.light, true);

                            // Because the document modifications do not explain this peptide, a set of
                            // explicit modifications must be constructed, even if they are empty.
                            IList<ExplicitMod> staticModList = new List<ExplicitMod>();
                            IEnumerable<ModificationInfo> modList = GetModifications(_peptides[index]);
                            foreach (var modInfo in modList)
                            {
                                var smod = new StaticMod("temp",
                                                         modInfo.ModifiedAminoAcid.ToString(),
                                                         null,
                                                         null,
                                                         LabelAtoms.None,
                                                         modInfo.ModifiedMass,
                                                         modInfo.ModifiedMass);
                                var exmod = new ExplicitMod(modInfo.IndexMod, smod);
                                staticModList.Add(exmod);
                            }

                            mods = new ExplicitMods(peptide, staticModList, new TypedExplicitModifications[0]);
                        }

                        // Make sure the types and charges in the settings are at the head
                        // of these lists to give them top priority, and get rankings correct.
                        int i = 0;
                        foreach (IonType type in rankTypes)
                        {
                            if (types.Remove(type))
                                types.Insert(i++, type);
                        }
                        i = 0;
                        foreach (int charge in rankCharges)
                        {
                            if (charges.Remove(charge))
                                charges.Insert(i++, charge);
                        }

                        var spectrumInfoR = new LibraryRankedSpectrumInfo(spectrum,
                                                                          transitionGroup.LabelType,
                                                                          transitionGroup,
                                                                          settings,
                                                                          mods,
                                                                          charges,
                                                                          types,
                                                                          rankCharges,
                                                                          rankTypes);

                        GraphItem = new ViewLibSpectrumGraphItem(spectrumInfoR, transitionGroup, _selectedLibrary)
                        {
                            ShowTypes = types,
                            ShowCharges = charges,
                            ShowRanks = Settings.Default.ShowRanks,
                            ShowMz = Settings.Default.ShowIonMz,
                            ShowObservedMz = Settings.Default.ShowObservedMz,
                            ShowDuplicates = Settings.Default.ShowDuplicateIons,
                            FontSize = Settings.Default.SpectrumFontSize,
                            LineWidth = Settings.Default.SpectrumLineWidth
                        };

                        graphControl.IsEnableVPan = graphControl.IsEnableVZoom =
                                                    !Settings.Default.LockYAxis;
                        AddGraphItem(graphPane, GraphItem);

                        available = true;
                    }
                }
            }
            catch (IOException)
            {
                AddGraphItem(graphPane, new NoDataMSGraphItem("Failure loading spectrum. Library may be corrupted."));
                return;
            }

            // Show unavailable message, if no spectrum loaded
            if (!available)
            {
                AddGraphItem(graphPane, new UnavailableMSGraphItem());
            }

            btnAIons.Checked = Settings.Default.ShowAIons;
            btnBIons.Checked = Settings.Default.ShowBIons;
            btnCIons.Checked = Settings.Default.ShowCIons;
            btnXIons.Checked = Settings.Default.ShowXIons;
            btnYIons.Checked = Settings.Default.ShowYIons;
            btnZIons.Checked = Settings.Default.ShowZIons;
            charge1Button.Checked = Settings.Default.ShowCharge1;
            charge2Button.Checked = Settings.Default.ShowCharge2;
        }

        private void AddGraphItem(MSGraphPane pane, IMSGraphItemInfo item)
        {
            pane.Title.Text = item.Title;
            graphControl.AddGraphItem(pane, item);
            pane.CurveList[0].Label.IsVisible = false;
            graphControl.Refresh();
        }

        /// <summary>
        /// Gets the index of the peptide in the peptides array for the selected
        /// peptide in the peptides listbox. Returns -1 if none is found.
        /// </summary>
        private int GetIndexOfSelectedPeptide()
        {
            if (_currentRange.Count > 0)
            {
                string selPeptide = ((ViewLibraryPepInfo)listPeptide.SelectedItem).DisplayString;
                string selPeptideAASequence = Regex.Replace(selPeptide, REGEX_MODIFICATION_PATTERN, string.Empty);

                Range selPeptideRange = BinaryRangeSearch(selPeptideAASequence, new Range(_currentRange));
                if (selPeptideRange.Count > 0)
                {
                    int start = selPeptideRange.StartIndex;
                    int end = selPeptideRange.EndIndex;
                    for (int i = start; i <= end; i++)
                    {
                        if (selPeptide.Equals(_peptides[i].DisplayString))
                        {
                            return i;
                        }
                    }
                }
            }

            return -1;
        }

        #endregion

        # region IStateProvider Implementation

        public IList<IonType> ShowIonTypes
        {
            get { return GraphSettings.ShowIonTypes; }
        }

        public IList<int> ShowIonCharges
        {
            get { return GraphSettings.ShowIonCharges; }
        }

        public void BuildSpectrumMenu(ZedGraphControl zedGraphControl, ContextMenuStrip menuStrip)
        {
            // Store original menuitems in an array, and insert a separator
            var items = new ToolStripItem[menuStrip.Items.Count];
            int iUnzoom = -1;
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = menuStrip.Items[i];
                var tag = (string)items[i].Tag;
                if (tag == "unzoom")
                    iUnzoom = i;
            }

            if (iUnzoom != -1)
                menuStrip.Items.Insert(iUnzoom, toolStripSeparator27);

            // Insert skyline specific menus
            var set = Settings.Default;
            int iInsert = 0;
            aionsContextMenuItem.Checked = set.ShowAIons;
            menuStrip.Items.Insert(iInsert++, aionsContextMenuItem);
            bionsContextMenuItem.Checked = set.ShowBIons;
            menuStrip.Items.Insert(iInsert++, bionsContextMenuItem);
            cionsContextMenuItem.Checked = set.ShowCIons;
            menuStrip.Items.Insert(iInsert++, cionsContextMenuItem);
            xionsContextMenuItem.Checked = set.ShowXIons;
            menuStrip.Items.Insert(iInsert++, xionsContextMenuItem);
            yionsContextMenuItem.Checked = set.ShowYIons;
            menuStrip.Items.Insert(iInsert++, yionsContextMenuItem);
            zionsContextMenuItem.Checked = set.ShowZIons;
            menuStrip.Items.Insert(iInsert++, zionsContextMenuItem);
            precursorIonContextMenuItem.Checked = set.ShowPrecursorIon;
            menuStrip.Items.Insert(iInsert++, precursorIonContextMenuItem);
            menuStrip.Items.Insert(iInsert++, toolStripSeparator11);
            menuStrip.Items.Insert(iInsert++, chargesContextMenuItem);
            if (chargesContextMenuItem.DropDownItems.Count == 0)
            {
                chargesContextMenuItem.DropDownItems.AddRange(new[]
                    {
                        charge1ContextMenuItem,
                        charge2ContextMenuItem,
                        charge3ContextMenuItem,
                        charge4ContextMenuItem,
                    });
            }
            menuStrip.Items.Insert(iInsert++, toolStripSeparator12);
            ranksContextMenuItem.Checked = set.ShowRanks;
            menuStrip.Items.Insert(iInsert++, ranksContextMenuItem);
            ionMzValuesContextMenuItem.Checked = set.ShowIonMz;
            menuStrip.Items.Insert(iInsert++, ionMzValuesContextMenuItem);
            observedMzValuesContextMenuItem.Checked = set.ShowObservedMz;
            menuStrip.Items.Insert(iInsert++, observedMzValuesContextMenuItem);
            duplicatesContextMenuItem.Checked = set.ShowDuplicateIons;
            menuStrip.Items.Insert(iInsert++, duplicatesContextMenuItem);
            menuStrip.Items.Insert(iInsert++, toolStripSeparator13);
            lockYaxisContextMenuItem.Checked = set.LockYAxis;
            menuStrip.Items.Insert(iInsert++, lockYaxisContextMenuItem);
            menuStrip.Items.Insert(iInsert++, toolStripSeparator14);
            menuStrip.Items.Insert(iInsert++, spectrumPropsContextMenuItem);
            menuStrip.Items.Insert(iInsert, toolStripSeparator15);

            // Remove some ZedGraph menu items not of interest
            foreach (var item in items)
            {
                var tag = (string)item.Tag;
                if (tag == "set_default" || tag == "show_val")
                    menuStrip.Items.Remove(item);
            }
            CopyEmfToolStripMenuItem.AddToContextMenu(graphControl, menuStrip);
        }

        public void LockYAxis(bool lockY)
        {
            graphControl.IsEnableVPan = graphControl.IsEnableVZoom = !lockY;
            graphControl.Refresh();
        }

        #endregion

        #region Draw Peptide List Box

        private IEnumerable<TextSequence> GetTextSequences(DrawItemEventArgs e)
        {
            ViewLibraryPepInfo pepInfo = ((ViewLibraryPepInfo)listPeptide.Items[e.Index]);
            IList<TextSequence> textSequences = new List<TextSequence>();
            // If a matching peptide exists, use the text sequences for that peptide.
            if (pepInfo.HasPeptide)
            {
                textSequences = PeptideTreeNode.CreateTextSequences(pepInfo.PeptideNode, Document.Settings,
                    pepInfo.GetPlainDisplayString(_lookupPool), null, new ModFontHolder(this));
            }
            // If no modifications, use a single plain test sequence
            else if (!pepInfo.IsModified)
            {
                textSequences.Add(CreateTextSequence(pepInfo.DisplayString, false));
            }
            // Otherwise bold-underline all modifications
            else
            {
                var sb = new StringBuilder(128);
                bool inMod = false;

                string sequence = pepInfo.DisplayString;
                for (int i = 0; i < sequence.Length; i++)
                {
                    bool isMod = i < sequence.Length - 1 && sequence[i + 1] == '[';
                    if (isMod != inMod)
                    {
                        textSequences.Add(CreateTextSequence(sb.ToString(), inMod));
                        sb.Remove(0, sb.Length);
                    }
                    sb.Append(sequence[i]);
                    inMod = isMod;
                    if (isMod)
                        i = sequence.IndexOf(']', i);
                }
                textSequences.Add(CreateTextSequence(sb.ToString(), inMod));
            }

            // Calculate placement for each text sequence
            int textRectWidth = 0;
            foreach (var textSequence in textSequences)
            {
                Size sizeMax = new Size(int.MaxValue, int.MaxValue);
                textSequence.Position = textRectWidth;
                textSequence.Width = TextRenderer.MeasureText(e.Graphics, textSequence.Text,
                    textSequence.Font, sizeMax, FORMAT_CUSTOM).Width;
                textRectWidth += textSequence.Width;
            }

            return textSequences;
        }
        
        private TextSequence CreateTextSequence(string text, bool modified)
        {
            var font = (modified ? ModFonts.Light : ModFonts.Plain);
            return new TextSequence { Text = text, Font = font, Color = Color.Black };
        }

        private void listPeptide_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1)
                return;

            // Draw the background of the ListBox control for each item.
            bool selected = e.Index == listPeptide.SelectedIndex;
            Color backColor = !_activated && selected ? Color.LightGray : e.BackColor;
            e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);

            if (cbShowModMasses.Visible && Settings.Default.ShowModMassesInExplorer)
            {
                // Draw the current item text based on the current Font 
                // and a black brush.
                TextRenderer.DrawText(e.Graphics, ((ViewLibraryPepInfo)listPeptide.Items[e.Index]).DisplayString,
                                      e.Font, e.Bounds, e.ForeColor, backColor,
                                      FORMAT_PLAIN);
            }
            else
            {
                // Draw formatted text and the peptide image, if a peptide has been successfully
                // associated with this item
                ViewLibraryPepInfo pepInfo = ((ViewLibraryPepInfo)listPeptide.Items[e.Index]);
                Rectangle bounds = e.Bounds;
                var imgWidth = _peptideImg.Width;
                if (pepInfo.PeptideNode != null)
                    e.Graphics.DrawImage(_peptideImg, bounds.Left, bounds.Top, imgWidth, bounds.Height);
                Rectangle rectDraw = new Rectangle(0, bounds.Y, 0, bounds.Height);
                foreach (var textSequence in GetTextSequences(e))
                {
                    rectDraw.X = textSequence.Position + bounds.X + PADDING + imgWidth;
                    rectDraw.Width = textSequence.Width;
                    var textColor = Equals(e.ForeColor, SystemColors.HighlightText) ? e.ForeColor : textSequence.Color;
                    TextRenderer.DrawText(e.Graphics, textSequence.Text,
                                          textSequence.Font, rectDraw, textColor, backColor,
                                          FORMAT_CUSTOM);
                }
            }
        }

        #endregion
        
        #region Change Events

        private void textPeptide_TextChanged(object sender, EventArgs e)
        {
            _currentRange = BinaryRangeSearch(textPeptide.Text, new Range(0, _peptides.Length - 1));
            UpdatePageInfo();
            UpdateStatusArea();
            UpdateListPeptide(0);
            UpdateUI();
        }

        private void listPeptide_SelectedIndexChanged(object sender, EventArgs e)
        {
//            _listPeptidePrevSelIndex = listPeptide.SelectedIndex;

            // We need to update the spectrum graph when the peptide
            // selected in the listbox changes.
            UpdateUI();
        }

        private void LibraryComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // The user has selected a different library. We need to reload 
            // everything in the dialog. 
            if (comboLibrary.SelectedItem.ToString() != _selectedLibName)
                ChangeSelectedLibrary(comboLibrary.SelectedItem.ToString());
        }

        public void ChangeSelectedLibrary(string libName)
        {
            comboLibrary.SelectedItem = libName;
            _selectedLibName = libName;
            UpdateViewLibraryDlg();
        }

        private void cbShowModMasses_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.ShowModMassesInExplorer = cbShowModMasses.Checked;
            int selPepIndex = listPeptide.SelectedIndex > 0 ? listPeptide.SelectedIndex : 0;
            UpdateListPeptide(selPepIndex);
        }

        private void OnDocumentChange(object sender, DocumentChangedEventArgs e)
        {
            // Only need to update if ViewLibraryDlg is already loaded and modifications have changed.
            if (_peptides != null
                && !Equals(e.DocumentPrevious.Settings.PeptideSettings.Modifications, Document.Settings.PeptideSettings.Modifications))
                UpdateListPeptide(listPeptide.SelectedIndex);
        }
        
        #endregion
        
        # region Mouse Click Events

        private void aionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowAIons = !GraphSettings.ShowAIons;
        }

        private void bionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowBIons = !GraphSettings.ShowBIons;
        }

        private void cionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowCIons = !GraphSettings.ShowCIons;
        }

        private void xionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowXIons = !GraphSettings.ShowXIons;
        }

        private void yionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowYIons = !GraphSettings.ShowYIons;
        }

        private void zionsContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowZIons = !GraphSettings.ShowZIons;
        }

        private void precursorIonContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowPrecursorIon = !GraphSettings.ShowPrecursorIon;
        }

        private void charge1ContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowCharge1 = !GraphSettings.ShowCharge1;
        }

        private void charge2ContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowCharge2 = !GraphSettings.ShowCharge2;
        }

        private void charge3ContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowCharge3 = !GraphSettings.ShowCharge3;
        }

        private void charge4ContextMenuItem_Click(object sender, EventArgs e)
        {
            GraphSettings.ShowCharge4 = !GraphSettings.ShowCharge4;
        }

        private void chargesMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            var set = GraphSettings;
            charge1ContextMenuItem.Checked = set.ShowCharge1;
            charge2ContextMenuItem.Checked = set.ShowCharge2;
            charge3ContextMenuItem.Checked = set.ShowCharge3;
            charge4ContextMenuItem.Checked = set.ShowCharge4;
        }

        private void ranksContextMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.ShowRanks = !Settings.Default.ShowRanks;
            UpdateUI();
        }

        private void ionMzValuesContextMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.ShowIonMz = !Settings.Default.ShowIonMz;
            UpdateUI();
        }

        private void observedMzValuesContextMenuItem_Click(object sender, EventArgs e)
        {
            SetObservedMzValues(!Settings.Default.ShowObservedMz);
        }

        public void SetObservedMzValues(bool on)
        {
            Settings.Default.ShowObservedMz = on;
            UpdateUI();
        }

        private void duplicatesContextMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.ShowDuplicateIons = duplicatesContextMenuItem.Checked;
            UpdateUI();
        }

        private void lockYaxisContextMenuItem_Click(object sender, EventArgs e)
        {
            // Avoid updating the rest of the graph just to change the y-axis lock state
            LockYAxis(Settings.Default.LockYAxis = lockYaxisContextMenuItem.Checked);
        }

        private void spectrumPropsContextMenuItem_Click(object sender, EventArgs e)
        {
            using (var dlg = new SpectrumChartPropertyDlg())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    UpdateUI();
            }
        }

        private void zoomSpectrumContextMenuItem_Click(object sender, EventArgs e)
        {
            ZoomSpectrumToSettings();
        }
        
        public void ZoomSpectrumToSettings()
        {
            var axis = GraphPane.XAxis;
            var instrument = Program.ActiveDocumentUI.Settings.TransitionSettings.Instrument;
            axis.Scale.Min = instrument.MinMz;
            axis.Scale.MinAuto = false;
            axis.Scale.Max = instrument.MaxMz;
            axis.Scale.MaxAuto = false;
            graphControl.Refresh();
        }

        private void copyMetafileButton_Click(object sender, EventArgs e)
        {
            CopyEmfToolStripMenuItem.CopyEmf(graphControl);
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            graphControl.Copy(false);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            graphControl.SaveAs();
        }

        private void btnPrint_Click(object sender, EventArgs e)
        {
            graphControl.DoPrint();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            CancelDialog();
        }

        public void CancelDialog()
        {
            DialogResult = DialogResult.Cancel;
            Application.DoEvents();
            Close();
        }

        // Adds a single library peptide to the document.
        private void btnAdd_Click(object sender, EventArgs e)
        {
            AddPeptide();

            // Set focus back to the peptide edit box to allow further navigation
            // of the peptide list, and possibly more adding of peptides.
            textPeptide.Focus();
        }

        public void AddPeptide()
        {
            if (CheckLibraryInSettings() == DialogResult.Cancel)
                return;

            var startingDocument = Document;

            var pepInfo = (ViewLibraryPepInfo)listPeptide.SelectedItem;
            var pepMatcher = new ViewLibraryPepMatching(startingDocument,
                                                        _selectedLibrary,
                                                        _selectedSpec,
                                                        _lookupPool,
                                                        _peptides);

            if (!EnsureBackgroundProteome(startingDocument, pepMatcher))
                return;

            var nodePepMatched = pepMatcher.MatchSinglePeptide(pepInfo);
            if (nodePepMatched == null)
            {
                MessageDlg.Show(this, "Modifications for this peptide do not match current document settings.");
                return;
            }
            if (nodePepMatched.Children.Count > 0)
            {
                var keyMatched = nodePepMatched.SequenceKey;
                int chargeMatched = ((TransitionGroupDocNode)nodePepMatched.Children[0]).TransitionGroup.PrecursorCharge;
                if (!cbAssociateProteins.Checked && Document.Peptides.Contains(nodePep => Equals(nodePep.SequenceKey, keyMatched) && nodePep.HasChildCharge(chargeMatched)))
                {
                    MessageDlg.Show(this, string.Format("The peptide {0} already exists with charge {1} in the current document.", nodePepMatched.Peptide, chargeMatched));
                    return;
                }
            }

            if (pepMatcher.EnsureDuplicateProteinFilter(this, true) == DialogResult.Cancel)
                return;

            IdentityPath toPath = Program.MainWindow.SelectedPath;
            IdentityPath selectedPath = toPath;
            
            string message = string.Format("Add library peptide {0}", nodePepMatched.Peptide.Sequence);
            Program.MainWindow.ModifyDocument(message, doc =>
                pepMatcher.AddPeptides(doc, null, toPath, out selectedPath));
            
            Program.MainWindow.SelectedPath = selectedPath;
        }

        private bool EnsureBackgroundProteome(SrmDocument document, ViewLibraryPepMatching pepMatcher)
        {
            if (cbAssociateProteins.Checked)
            {
                var backgroundProteome = document.Settings.PeptideSettings.BackgroundProteome;
                if (backgroundProteome.BackgroundProteomeSpec.IsNone) 
                {
                    MessageDlg.Show(this, "A background proteome is required to associate proteins.");
                    return false;
                }
                if(backgroundProteome.GetDigestion(document.Settings.PeptideSettings) == null)
                {
                    MessageDlg.Show(this, "The background proteome must be digested in order to associate proteins.");
                    return false;
                }
                pepMatcher.SetBackgroundProteome(backgroundProteome);
            }
            return true;
        }

        public DialogResult CheckLibraryInSettings()
        {
            // Check to see if the library is part of the settings. If not, prompt the user to add it.
            var docLibraries = Document.Settings.PeptideSettings.Libraries;
            if (docLibraries.GetLibrary(_selectedLibName) == null)
            {
                using (var libraryNotAddedMsgDlg =
                    new MultiButtonMsgDlg(
                        string.Format(
                            "The library {0} is not currently added to your document.\nWould you like to add it?",
                            _selectedLibName), "Yes", "No"))
                {
                    var result = libraryNotAddedMsgDlg.ShowDialog(this);
                    if (result == DialogResult.Cancel)
                        return result;
                    if (result == DialogResult.Yes)
                        Program.MainWindow.ModifyDocument("Add Library", doc =>
                                                                         doc.ChangeSettings(
                                                                             doc.Settings.ChangePeptideLibraries(
                                                                                 pepLibraries =>
                                                                                 pepLibraries.ChangeLibraries(
                                                                                     new List<LibrarySpec>(
                                                                                         docLibraries.LibrarySpecs)
                                                                                         {_selectedSpec},
                                                                                     new List<Library>(
                                                                                         docLibraries.Libraries)
                                                                                         {_selectedLibrary}))));
                }
            }
            return DialogResult.OK;
        }

        /// <summary>
        /// Adds all matched library peptides to the current document.
        /// </summary>
        private void btnAddAll_Click(object sender, EventArgs e)
        {
            AddAllPeptides();
        }

        public void AddAllPeptides()
        {
            if(CheckLibraryInSettings() == DialogResult.Cancel)
                return;
            
            var startingDocument = Document;

            var pepMatcher = new ViewLibraryPepMatching(startingDocument,
                                                        _selectedLibrary,
                                                        _selectedSpec,
                                                        _lookupPool,
                                                        _peptides);

            if (!EnsureBackgroundProteome(startingDocument, pepMatcher))
                return;

            pepMatcher.AddAllPeptidesSelectedPath = Program.MainWindow.SelectedPath;

            LongWaitDlg longWaitDlg = new LongWaitDlg
                                          {
                                              Text = "Matching Peptides",
                                              Message = "Matching peptides to the current document settings"
                                          };
            longWaitDlg.PerformWork(this, 1000, pepMatcher.AddAllPeptidesToDocument);
            var newDocument = pepMatcher.DocAllPeptides;
            if (longWaitDlg.IsCanceled || newDocument == null)
                return;

            var selectedPath = pepMatcher.AddAllPeptidesSelectedPath;
            var numMatchedPeptides = pepMatcher.MatchedPeptideCount;
            if (numMatchedPeptides == 0)
            {
                MessageDlg.Show(this, "No peptides match the current document settings.");
                return;
            }

            // Calculate changes that will occur.
            var peptideCountDiff = newDocument.PeptideCount - startingDocument.PeptideCount;
            var groupCountDiff = newDocument.TransitionGroupCount - startingDocument.TransitionGroupCount;
            if (peptideCountDiff + groupCountDiff == 0)
            {
                MessageDlg.Show(this, "All library peptides already exist in the current document.");
                return;
            }
            var pepGroupCountDiff = newDocument.PeptideGroupCount - startingDocument.PeptideGroupCount;
            string proteins = cbAssociateProteins.Checked ? string.Format("{0} proteins,", pepGroupCountDiff)
                : "";
            string msg = string.Format(
                    "This operation will add {0} {1} peptides, {2} precursors and {3} transitions to the document.",
                    proteins, peptideCountDiff, groupCountDiff, 
                    newDocument.TransitionCount - startingDocument.TransitionCount);
            var numSkipped = pepMatcher.SkippedPeptideCount;          
            var hasSkipped = numSkipped > 0;
            var numUnmatchedPeptides = PeptidesCount - numMatchedPeptides;
            var hasUnmatched = numUnmatchedPeptides > 0;
            if (hasSkipped || hasUnmatched)
            {
                string duplicatePeptides = hasSkipped ? string.Format("{0} existing", numSkipped) : "";
                string unmatchedPeptides = hasUnmatched ? string.Format("{0} unmatched", numUnmatchedPeptides) : "";
                string entrySuffix = (numSkipped + numUnmatchedPeptides > 1 ? "ies" : "y");
                string format = (hasSkipped && hasUnmatched) ?
                    "{0}\n\n{1} and {2} library entr{3} will be ignored." :
                    "{0}\n\n{1}{2} library entr{3} will be ignored.";
                msg = string.Format(format, msg, duplicatePeptides, unmatchedPeptides, entrySuffix);
            }
            using (var addLibraryPepsDlg = new MultiButtonMsgDlg(msg, "Add All") { Tag = numUnmatchedPeptides })
            {
                if (addLibraryPepsDlg.ShowDialog(this) == DialogResult.Cancel)
                    return;
            }
            // If the user chooses to continue with the operation, call AddPeptides again in case the document has changed.
            var toPath = Program.MainWindow.SelectedPath;
            Program.MainWindow.ModifyDocument(string.Format("Add all peptides from {0} library", SelectedLibraryName),
                doc =>
                {
                    if (ReferenceEquals(doc, startingDocument))
                        return newDocument;
                    if (!Equals(doc.Settings.PeptideSettings.Modifications, startingDocument.Settings.PeptideSettings.Modifications))
                    {
                        selectedPath = toPath;
                        throw new InvalidDataException("The document changed during processing.\nPlease retry this operation.");
                    }
                    return pepMatcher.AddPeptides(doc, null, toPath, out selectedPath);
                });

            Program.MainWindow.SelectedPath = selectedPath;
        }

        // User wants to go to the previous page. Update the page info.
        private void PreviousLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            _pageInfo.Page--;
            NextLink.Enabled = true;
            PreviousLink.Enabled = _pageInfo.Page > 1;
            UpdateListPeptide(0);
            UpdateStatusArea();
            UpdateUI();
            textPeptide.Focus();
        }

        // User wants to go to the next page. Update the page info.
        private void NextLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            _pageInfo.Page++;
            PreviousLink.Enabled = true;
            NextLink.Enabled = (_currentRange.Count - (_pageInfo.Page * _pageInfo.PageSize)) > 0;
            UpdateListPeptide(0);
            UpdateStatusArea();
            UpdateUI();
            textPeptide.Focus();
        }

        private void btnLibDetails_Click(object sender, EventArgs e)
        {
            if (_selectedLibrary != null)
            {
                LibraryDetails libInfo = _selectedLibrary.LibraryDetails;
                SpectrumLibraryInfoDlg dlg = new SpectrumLibraryInfoDlg(libInfo);
                // dlg.SetLinks(links);
                dlg.ShowDialog(this);
            }
        }

        # endregion
               
        #region Splitter events

        // Temp variable to store a previously focused control
        private Control _focused;

        private void splitMain_MouseDown(object sender, MouseEventArgs e)
        {
            // Get the focused control before the splitter is focused
            _focused = GetFocused(Controls);
        }

        private void splitMain_MouseUp(object sender, MouseEventArgs e)
        {
            // If a previous control had focus
            if (_focused != null)
            {
                // Return focus and clear the temp variable
                _focused.Focus();
                _focused = null;
            }
        }

        private static Control GetFocused(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                if (c.Focused)
                {
                    // Return the focused control
                    return c;
                }
                if (c.ContainsFocus)
                {
                    // If the focus is contained inside a control's children
                    // return the child
                    return GetFocused(c.Controls);
                }
            }
            // No control on the form has focus
            return null;
        }

        #endregion

        #region Key Down Events

        private void PeptideTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
            {
                e.Handled = true;
                if (listPeptide.SelectedIndex > 0)
                {
                    listPeptide.SelectedIndex--;
                }
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                if ((listPeptide.SelectedIndex + 1) < listPeptide.Items.Count)
                {
                    listPeptide.SelectedIndex++;
                }
            }
        }

// ReSharper disable MemberCanBeMadeStatic.Local
        private void ViewLibraryDlg_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.Z)
                    Program.MainWindow.Undo();
                if (e.KeyCode == Keys.Y)
                    Program.MainWindow.Redo();
            }
        }
// ReSharper restore MemberCanBeMadeStatic.Local

        #endregion

        #region Node Tip


        private void listPeptide_MouseMove(object sender, MouseEventArgs e)
        {
            Point pt = e.Location;
            if (!_moveThreshold.Moved(pt))
                return;
            _moveThreshold.Location = null;

            ITipProvider tipProvider = null;
            int i = GetIndexAtPoint(pt);

            if (i == -1)
            {
                _lastTipNode = null;
                _lastTipProvider = null;
            }
            else
            {
                var pepInfo = ((ViewLibraryPepInfo)listPeptide.Items[i]);
                if (!Equals(pepInfo, _lastTipNode))
                {
                    _lastTipNode = pepInfo;
                    _lastTipProvider = new PeptideTipProvider(pepInfo);
                }
                tipProvider = _lastTipProvider;
            }

            if (tipProvider == null || !tipProvider.HasTip)
                _nodeTip.HideTip();
            else
            {
                var itemRect = listPeptide.GetItemRectangle(i);
                _nodeTip.SetTipProvider(tipProvider, 
                    new Rectangle(itemRect.X + _peptideImg.Width, itemRect.Y, itemRect.Width - _peptideImg.Width, itemRect.Height), 
                    pt);
            }
        }

        private void listPeptide_MouseLeave(object sender, EventArgs e)
        {
            _nodeTip.HideTip();
        }

        private int GetIndexAtPoint(Point pt)
        {
            for (int i = listPeptide.TopIndex; 0 <= i && i < listPeptide.Items.Count; i++)
            {
                var rectItem = listPeptide.GetItemRectangle(i);
                if (rectItem.Top > ClientRectangle.Bottom)
                    break;

                if (rectItem.Contains(pt))
                    return i;
            }
            return -1;
        }

        public Rectangle ScreenRect
        {
            get { return Screen.GetBounds(listPeptide); }
        }

        public bool AllowDisplayTip
        {
            get { return true; }
        }

        public Rectangle RectToScreen(Rectangle r)
        {
            return listPeptide.RectangleToScreen(r);
        }

        #endregion

        # region ViewLibraryPepInfo Helpers

        // Gets the display string for the given ViewLibraryPepInfo object, minus the
        // modification characters.
        private string GetUnmodifiedDisplayString(ViewLibraryPepInfo pep)
        {
            return pep.GetAASequence(_lookupPool) + Transition.GetChargeIndicator(pep.Charge);
        }

        // Computes each ModificationInfo for the given peptide and returns a 
        // list of all modifications.
        private static IEnumerable<ModificationInfo> GetModifications(ViewLibraryPepInfo pep)
        {
            IList<ModificationInfo> modList = new List<ModificationInfo>();
            string sequence = pep.Sequence;
            int iMod = -1;
            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] != '[')
                {
                    iMod++;
                    continue;
                }
                int iAa = i - 1;
                // Invalid format. Should never happen.
                if (iAa < 0)
                    break;
                i++;
                int signVal = 1;
                if (sequence[i] == '+')
                    i++;
                else if (sequence[i] == '-')
                {
                    i++;
                    signVal = -1;
                }
                int iEnd = sequence.IndexOf(']', i);
                // Invalid format. Should never happen.
                if (iEnd == -1)
                    break;
                // NIST libraries sometimes use [?] when modification cannot be identified
                double massDiff;
                if (double.TryParse(sequence.Substring(i, iEnd - i), NumberStyles.Number, CultureInfo.InvariantCulture, out massDiff))
                {
                    modList.Add(new ModificationInfo(iMod, sequence[iAa], massDiff * signVal));
                }
                i = iEnd;
            }
            return modList;
        }

        // Compares the display string minus the modification characters for 
        // the given peptide with the string passed in.
        private int Compare(ViewLibraryPepInfo pep, string s)
        {
            return string.Compare(GetUnmodifiedDisplayString(pep), 0, s, 0, s.Length, true);
        }

        // Checks to see if the display string minus the modification
        // characters starts with the string passed in. 
        private bool IsMatch(ViewLibraryPepInfo pep, string s)
        {
            return GetUnmodifiedDisplayString(pep).StartsWith(s, StringComparison.InvariantCultureIgnoreCase);
        }

        #endregion

        #region Test helpers

        public string FilterString
        {
            get { return textPeptide.Text; }
            set { textPeptide.Text = value; }            
        }

        public int SelectedIndex
        {
            get { return listPeptide.SelectedIndex; }
            set { listPeptide.SelectedIndex = value; }
        }

        #endregion

        /// <summary>
        /// Data structure to store information on a modification for a given
        /// peptide sequence.
        /// </summary>
        private class ModificationInfo
        {
            public int IndexMod { get; private set; }
            public char ModifiedAminoAcid { get; private set; }
            public double ModifiedMass { get; private set; }

            public ModificationInfo(int indexMod, char modifiedAminoAcid, double modifiedMass)
            {
                IndexMod = indexMod;
                ModifiedAminoAcid = modifiedAminoAcid;
                ModifiedMass = modifiedMass;
            }
        }

        /// <summary>
        /// IComparer implementation to compare two ViewLibraryPepInfo objects. It is used
        /// to sort the array of ViewLibraryPepInfo objects we load from each library so 
        /// that we can show it in alphabetical order in the list, and also 
        /// show the modified peptides right below the unmodified versions. 
        /// Here's an example:
        /// DEYACR+
        /// DEYAC[+57.0]R+
        /// DEYACR++
        /// DEYAC[+57.0]R++
        /// </summary>
        private class PepInfoComparer : IComparer<ViewLibraryPepInfo>
        {
            private readonly List<byte> _lookupPool;

            // Constructs a comparer using the specified CompareOptions.
            public PepInfoComparer(List<byte> lookupPool)
            {
                _lookupPool = lookupPool;
            }

            // Compares peptides in PeptideInfo according to the CompareOptions
            // specified in the constructor.
            public int Compare(ViewLibraryPepInfo p1, ViewLibraryPepInfo p2)
            {
                return ViewLibraryPepInfo.Compare(p1, p2, _lookupPool);
            }
        }

        /// <summary>
        /// Data structure to store a range of indices of peptides found in 
        /// the peptides array matching the sequence typed in by the user.
        /// </summary>
        private class Range
        {
            public Range(int start, int end)
            {
                StartIndex = start;
                EndIndex = end;
            }

            public Range(Range rangeIn)
            {
                StartIndex = rangeIn.StartIndex;
                EndIndex = rangeIn.EndIndex;
            }

            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
            public int Count
            {
                get
                {
                    if ((StartIndex != -1) && (EndIndex != -1))
                    {
                        return (EndIndex + 1) - StartIndex;
                    }
                    return 0;
                }
            }
        }

        /// <summary>
        /// If the library has a HUGE number of peptides, it may not be
        /// performant to show them all at once in the peptides list box.
        /// We use paging and show 100 peptides at a time. This data structure
        /// keeps track of information pertaining to the pages.
        /// </summary>
        private class PageInfo
        {
            // The current page we are on
            public int Page { get; set; }

            // The size of each page
            private readonly int _pageSize;
            public int PageSize { get { return _pageSize; } }

            // Range of indices of items to be shown from the peptides array
            private Range _itemIndexRange;
            public Range ItemIndexRange { set { _itemIndexRange = value; } }

            // Total number of items to be show from the peptides array
            public int Items { get { return _itemIndexRange.Count; } }

            public PageInfo(int pageSize, int currentPage, Range itemIndexRange)
            {
                _pageSize = pageSize;
                Page = currentPage;
                ItemIndexRange = itemIndexRange;
            }

            // The total number of pages we need to show all the items
            public int Pages
            {
                get
                {
                    int temp = Items;
                    int totalPages = 0;
                    while (temp > 0)
                    {
                        totalPages++;
                        temp -= PageSize;
                    }
                    return totalPages;
                }
            }

            // The start index in the peptides array for the current page
            public int StartIndex
            {
                get
                {
                    int start = -1;
                    if (Items > 0)
                    {
                        if (Page > 0)
                        {
                            start = _itemIndexRange.StartIndex + (Page - 1) * PageSize;
                        }
                        else
                        {
                            start = _itemIndexRange.StartIndex;
                        }
                    }
                    return start;
                }
            }

            // The end index in the peptides array for the current page
            public int EndIndex
            {
                get
                {
                    int end = -1;
                    if (Items > 0)
                    {
                        if (Page == Pages)
                        {
                            end = _itemIndexRange.EndIndex + 1;
                        }
                        else
                        {
                            end = _itemIndexRange.StartIndex + (Page * PageSize);
                        }
                    }
                    return end;
                }
            }
        }

        /// <summary>
        /// Represents the spectrum graph for the selected peptide.
        /// </summary>
        public class ViewLibSpectrumGraphItem : AbstractSpectrumGraphItem
        {
            private readonly Library _library;
            private string LibraryName { get { return _library.Name; } }
            private TransitionGroup TransitionGroup { get; set; }

            public ViewLibSpectrumGraphItem(LibraryRankedSpectrumInfo spectrumInfo, TransitionGroup group, Library lib)
                : base(spectrumInfo)
            {
                TransitionGroup = group;
                _library = lib;
            }

            protected override bool IsMatch(double predictedMz)
            {
                return false;
            }

            public override string Title
            {
                get
                {
                    string libraryNamePrefix = LibraryName;
                    if (!string.IsNullOrEmpty(libraryNamePrefix))
                        libraryNamePrefix += " - ";

                    string sequence = TransitionGroup.Peptide.Sequence;
                    int charge = TransitionGroup.PrecursorCharge;

                    return string.Format("{0}{1}, Charge {2}", libraryNamePrefix, sequence, charge);
                }
            }
        }

        /// <summary>
        /// ILoadMonitor implementation needed for loading the library in the 
        /// case where the library isn't already loaded in the main window.
        /// </summary>
        private sealed class ViewLibLoadMonitor : ILoadMonitor
        {
            private readonly IProgressMonitor _monitor;

            public ViewLibLoadMonitor(IProgressMonitor monitor)
            {
                _monitor = monitor;
            }

            public bool IsCanceled
            {
                get { return _monitor.IsCanceled; }
            }

            public void UpdateProgress(ProgressStatus status)
            {
                _monitor.UpdateProgress(status);
            }

            public IStreamManager StreamManager
            {
                get
                {
                    return FileStreamManager.Default;
                }
            }
        }
        
        private class PeptideTipProvider : ITipProvider
        {
            private ViewLibraryPepInfo _pepInfo;

            public PeptideTipProvider(ViewLibraryPepInfo pepInfo)
            {
                _pepInfo = pepInfo;
            }

            public bool HasTip
            {
                get { return true; }
            }

            public Size RenderTip(Graphics g, Size sizeMax, bool draw)
            {
                var table = new TableDesc();
                SizeF size;
                using (RenderTools rt = new RenderTools())
                {
                    table.AddDetailRow("", _pepInfo.DisplayString.Replace(".0]", "]"), rt);
                    size = table.CalcDimensions(g);
                    if (draw)
                        table.Draw(g);
                }
                return new Size((int)size.Width + 2, (int)size.Height + 2);
            }
        }
    }
}
