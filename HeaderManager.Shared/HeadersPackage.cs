/* Copyright (c) rubicon IT GmbH
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
 * FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using HeaderManager.Core;
using HeaderManager.Core.Options;
using HeaderManager.Headers;
using HeaderManager.Interfaces;
using HeaderManager.MenuItemCommands.EditorMenu;
using HeaderManager.MenuItemCommands.FolderMenu;
using HeaderManager.MenuItemCommands.ProjectItemMenu;
using HeaderManager.MenuItemCommands.ProjectMenu;
using HeaderManager.MenuItemCommands.SolutionMenu;
using HeaderManager.Options;
using HeaderManager.Options.DialogPages;
using HeaderManager.Options.Model;
using HeaderManager.PopUp.FeaturesQuestion;
using HeaderManager.Utils;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace HeaderManager
{
  /// <summary>
  ///   This is the class that implements the package exposed by this assembly.
  ///   The minimum requirement for a class to be considered a valid package for Visual Studio
  ///   is to implement the IVsPackage interface and register itself with the shell.
  ///   This package uses the helper classes defined inside the Managed Package Framework (MPF)
  ///   to do it: it derives from the Package class that provides the implementation of the
  ///   IVsPackage interface and uses the registration attributes defined in the framework to
  ///   register itself and its components with the shell.
  /// </summary>
  // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
  // a package.
  [PackageRegistration (UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)] // register the information needed to show the this package
  [InstalledProductRegistration ("#110", "#112", Version, IconResourceID = 400)] // Help/About dialog of Visual Studio.
  [ProvideMenuResource ("Menus.ctmenu", 1)] // let the shell know that this package exposes some menus.
  [ProvideOptionPage (typeof (OptionsPage), c_licenseHeaders, c_general, 0, 0, true)]
  [ProvideOptionPage (typeof (DefaultHeaderPage), c_licenseHeaders, c_defaultHeader, 0, 0, true)]
  [ProvideOptionPage (typeof (LanguagesPage), c_licenseHeaders, c_languages, 0, 0, true)]
  [ProvideProfile (typeof (OptionsPage), c_licenseHeaders, c_general, 0, 0, true)]
  [ProvideProfile (typeof (DefaultHeaderPage), c_licenseHeaders, c_defaultHeader, 0, 0, true)]
  [ProvideProfile (typeof (LanguagesPage), c_licenseHeaders, c_languages, 0, 0, true)]
  [ProvideAutoLoad (VSConstants.UICONTEXT.SolutionOpening_string, PackageAutoLoadFlags.BackgroundLoad)]
  [Guid (c_guidHeadersPkgString)]
  public sealed class HeadersPackage : AsyncPackage, IHeaderExtension
  {
    public const string Version = "5.0.1";
    private const string c_guidHeadersPkgString = "4c570677-8476-4d33-bd0c-da36c89287c8";

    private const string c_licenseHeaders = "License Header Manager";
    private const string c_general = "General";
    private const string c_languages = "Languages";
    private const string c_defaultHeader = "Default Header";

    /// <summary>
    ///   GUID representing the output pane the <see cref="OutputPaneAppender" /> logs to.
    /// </summary>
    public static Guid GuidOutputPaneAppender = new Guid ("f5fb81c5-39f2-4c51-bbfd-9b5d83c13e1c");

    private static readonly ILog s_log = LogManager.GetLogger (MethodBase.GetCurrentMethod().DeclaringType);

    private Stack<ProjectItem> _addedItems;
    private CommandEvents _commandEvents;
    private CommandEvents _currentCommandEvents;

    private string _currentCommandGuid;
    private int _currentCommandId;
    private FileAppender _fileAppender;
    private IVsOutputWindow _outputPane;
    private OutputPaneAppender _outputPaneAppender;

    private ProjectItemsEvents _projectItemEvents;
    private ProjectItemsEvents _websiteItemEvents;

    /// <summary>
    ///   Default constructor of the package.
    ///   Inside this method you can place any initialization code that does not require
    ///   any Visual Studio service because at this point the package object is created but
    ///   not sited yet inside Visual Studio environment. The place to do all the other
    ///   initialization is the Initialize method.
    /// </summary>
    public HeadersPackage ()
    {
      Instance = this;
      _addedItems = new Stack<ProjectItem>();
    }

    /// <summary>
    ///   Gets the <see cref="IHeaderExtension" /> instance that was created upon initializing the package.
    /// </summary>
    /// <remarks>The actual type of this property is <see cref="HeadersPackage" />.</remarks>
    public static IHeaderExtension Instance { get; private set; }

    public HeaderReplacer HeaderReplacer
    {
      get
      {
        var keywords = GeneralOptionsPageModel.UseRequiredKeywords ? CoreOptions.RequiredKeywordsAsEnumerable (GeneralOptionsPageModel.RequiredKeywords) : null;
        return new HeaderReplacer (LanguagesPageModel.Languages, keywords);
      }
    }

    public IHeaderExtractor HeaderExtractor { get; private set; }

    public void ShowLanguagesPage ()
    {
      ShowOptionPage (typeof (LanguagesPage));
    }

    public void ShowOptionsPage ()
    {
      ShowOptionPage (typeof (OptionsPage));
    }

    public IDefaultHeaderPageModel DefaultHeaderPageModel => Options.Model.DefaultHeaderPageModel.Instance;

    public ILanguagesPageModel LanguagesPageModel => Options.Model.LanguagesPageModel.Instance;

    public IGeneralOptionsPageModel GeneralOptionsPageModel => Options.Model.GeneralOptionsPageModel.Instance;

    public DTE2 Dte2 { get; private set; }

    public new JoinableTaskFactory JoinableTaskFactory { get; private set; }

    public bool IsCalledByLinkedCommand { get; private set; }

    public bool SolutionHeaderDefinitionExists ()
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var solutionHeaderDefinitionFilePath = HeaderDefinitionFileHelper.GetHeaderDefinitionFilePathForSolution (Dte2.Solution);
      return File.Exists (solutionHeaderDefinitionFilePath);
    }

    public bool ShouldBeVisible (ProjectItem item)
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var visible = false;
      if (ProjectItemInspection.IsPhysicalFile (item))
        visible = HeaderReplacer.IsValidPathInput (item.FileNames[1]) && item.CanBeOpened();

      return visible;
    }

    public ProjectItem GetActiveProjectItem ()
    {
      try
      {
        var activeDocument = Dte2.ActiveDocument;
        return activeDocument?.ProjectItem;
      }
      catch (ArgumentException)
      {
        return null;
      }
    }

    public object GetSolutionExplorerItem ()
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var monitorSelection = (IVsMonitorSelection) GetGlobalService (typeof (SVsShellMonitorSelection));
      monitorSelection.GetCurrentSelection (out var hierarchyPtr, out var projectItemId, out _, out _);

      if (!(Marshal.GetTypedObjectForIUnknown (hierarchyPtr, typeof (IVsHierarchy)) is IVsHierarchy hierarchy))
        return null;

      hierarchy.GetProperty (projectItemId, (int) __VSHPROPID.VSHPROPID_ExtObject, out var item);
      return item;
    }

    /// <summary>
    ///   Initialization of the package; this method is called right after the package is sited, so this is the
    ///   place where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    protected override async Task InitializeAsync (CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
      AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_OnAssemblyResolve;
      JoinableTaskFactory = ThreadHelper.JoinableTaskFactory;
      HeaderExtractor = new HeaderExtractor();

      await base.InitializeAsync (cancellationToken, progress);
      await JoinableTaskFactory.SwitchToMainThreadAsync (cancellationToken);

      Dte2 = await GetServiceAsync (typeof (DTE)) as DTE2;
      Assumes.Present (Dte2);

      CreateAndConfigureFileAppender (Path.GetFileNameWithoutExtension (Dte2?.Solution.FullName));
      await CreateAndConfigureOutputPaneAppenderAsync();
      s_log.Info ("Logger has been initialized");

      _addedItems = new Stack<ProjectItem>();

      await AddHeaderToProjectItemCommand.InitializeAsync (this);
      await RemoveHeaderFromProjectItemCommand.InitializeAsync (this);
      await AddHeaderToAllFilesInSolutionCommand.InitializeAsync (this);
      await RemoveHeaderFromAllFilesInSolutionCommand.InitializeAsync (this);
      await AddNewSolutionHeaderDefinitionFileCommand.InitializeAsync (this, Dte2?.Solution, () => DefaultHeaderPageModel.HeaderFileText);
      await OpenSolutionHeaderDefinitionFileCommand.InitializeAsync (this);
      await RemoveSolutionHeaderDefinitionFileCommand.InitializeAsync (this);
      await AddHeaderToAllFilesInProjectCommand.InitializeAsync (this);
      await RemoveHeaderFromAllFilesInProjectCommand.InitializeAsync (this);
      await AddNewHeaderDefinitionFileToProjectCommand.InitializeAsync (this);
      await AddExistingHeaderDefinitionFileToProjectCommand.InitializeAsync (this);
      await HeaderOptionsCommand.InitializeAsync (this);
      await AddHeaderToAllFilesInFolderCommand.InitializeAsync (this);
      await RemoveHeaderFromAllFilesInFolderCommand.InitializeAsync (this);
      await AddExistingHeaderDefinitionFileToFolderCommand.InitializeAsync (this);
      await AddNewHeaderDefinitionFileToFolderCommand.InitializeAsync (this);
      await AddHeaderEditorAdvancedMenuCommand.InitializeAsync (this);
      await RemoveHeaderEditorAdvancedMenuCommand.InitializeAsync (this);

      //register ItemAdded event handler
      if (Dte2?.Events is Events2 events)
      {
        _projectItemEvents = events.ProjectItemsEvents; //we need to keep a reference, otherwise the object is garbage collected and the event won't be fired
        _projectItemEvents.ItemAdded += ItemAdded;

        //Register to WebsiteItemEvents for Website Projects to work
        //Reference: https://social.msdn.microsoft.com/Forums/en-US/dde7d858-2440-43f9-bbdc-3e1b815d4d1e/itemadded-itemremoved-and-itemrenamed-events-not-firing-in-web-projects?forum=vsx
        //Concerns, that the ItemAdded Event gets called on unrelated events, like closing the solution or opening folder, could not be reproduced
        try
        {
          _websiteItemEvents = events.GetObject ("WebSiteItemsEvents") as ProjectItemsEvents;
        }
        catch (Exception ex)
        {
          //This probably only throws an exception if no WebSite component is installed on the machine.
          //If no WebSite component is installed, they are probably not using a WebSite Project and therefore don't need that feature.
          s_log.Error ("No WebSite component is installed on the machine: ", ex);
        }

        if (_websiteItemEvents != null)
          _websiteItemEvents.ItemAdded += ItemAdded;
      }

      // migrate options from registry to config file
      await MigrateOptionsAsync();

      //register event handlers for linked commands
      var page = GeneralOptionsPageModel;
      if (page != null)
      {
        foreach (var command in page.LinkedCommands)
        {
          command.Events = Dte2.Events.CommandEvents[command.Guid, command.Id];

          switch (command.ExecutionTime)
          {
            case ExecutionTime.Before:
              command.Events.BeforeExecute += BeforeLinkedCommandExecuted;
              break;
            case ExecutionTime.After:
              command.Events.AfterExecute += AfterLinkedCommandExecuted;
              break;
            default:
              throw new ArgumentOutOfRangeException();
          }
        }

        page.LinkedCommandsChanged += CommandsChanged;

        //register global event handler for ItemAdded
        _commandEvents = Dte2.Events.CommandEvents;
        _commandEvents.BeforeExecute += BeforeAnyCommandExecuted;
      }
    }

    private Assembly CurrentDomain_OnAssemblyResolve (object sender, ResolveEventArgs args)
    {
      if (args.Name.Contains ("System.Threading.Tasks.Extensions"))
        return LoadAssembly ("System.Threading.Tasks.Extensions.dll");
      if (args.Name.Contains ("System.Runtime.CompilerServices.Unsafe"))
        return LoadAssembly ("System.Runtime.CompilerServices.Unsafe.dll");
      if (args.Name.Contains ("System.Buffers"))
        return LoadAssembly ("System.Buffers.dll");

      return null;
    }

    private Assembly LoadAssembly (string dllName)
    {
      var dllDirectory = Path.GetDirectoryName (Assembly.GetExecutingAssembly().Location);
      var dllPath = Path.Combine (dllDirectory ?? string.Empty, dllName);
      return Assembly.LoadFrom (dllPath);
    }

    private void BeforeLinkedCommandExecuted (string guid, int id, object customIn, object customOut, ref bool cancelDefault)
    {
      InvokeAddHeaderCommandFromLinkedCmd();
    }

    private void AfterLinkedCommandExecuted (string guid, int id, object customIn, object customOut)
    {
      InvokeAddHeaderCommandFromLinkedCmd();
    }

    private void InvokeAddHeaderCommandFromLinkedCmd ()
    {
      IsCalledByLinkedCommand = true;
      AddHeaderEditorAdvancedMenuCommand.Instance.Invoke();
      IsCalledByLinkedCommand = false;
    }

    private void CommandsChanged (object sender, NotifyCollectionChangedEventArgs e)
    {
      if (e.Action == NotifyCollectionChangedAction.Move)
        return;

      if (e.OldItems != null)
        foreach (LinkedCommand command in e.OldItems)
          switch (command.ExecutionTime)
          {
            case ExecutionTime.Before:
              command.Events.BeforeExecute -= BeforeLinkedCommandExecuted;
              break;
            case ExecutionTime.After:
              command.Events.AfterExecute -= AfterLinkedCommandExecuted;
              break;
            default:
              throw new ArgumentOutOfRangeException();
          }

      ThreadHelper.ThrowIfNotOnUIThread();
      if (e.NewItems != null)
        foreach (LinkedCommand command in e.NewItems)
        {
          command.Events = Dte2.Events.CommandEvents[command.Guid, command.Id];

          switch (command.ExecutionTime)
          {
            case ExecutionTime.Before:
              command.Events.BeforeExecute += BeforeLinkedCommandExecuted;
              break;
            case ExecutionTime.After:
              command.Events.AfterExecute += AfterLinkedCommandExecuted;
              break;
            default:
              throw new ArgumentOutOfRangeException();
          }
        }
    }

    private void BeforeAnyCommandExecuted (string guid, int id, object customIn, object customOut, ref bool cancelDefault)
    {
      //Save the current command in case it adds a new item to the project.
      _currentCommandGuid = guid;
      _currentCommandId = id;
    }

    private void ItemAdded (ProjectItem item)
    {
      //An item was added. Check if we should insert a header automatically.
      var page = GeneralOptionsPageModel;
      if (page == null || !page.InsertInNewFiles || item == null)
        return;

      ThreadHelper.ThrowIfNotOnUIThread();
      //Normally the header should be inserted here, but that might interfere with the command
      //currently being executed, so we wait until it is finished.
      _currentCommandEvents = Dte2.Events.CommandEvents[_currentCommandGuid, _currentCommandId];
      _currentCommandEvents.AfterExecute += FinishedAddingItem;
      _addedItems.Push (item);
    }

    private void FinishedAddingItem (string guid, int id, object customIn, object customOut)
    {
      FinishedAddingItemAsync().FireAndForget();
    }

    private async Task FinishedAddingItemAsync ()
    {
      await JoinableTaskFactory.SwitchToMainThreadAsync();
      //Now we can finally insert the header into the new item.
      while (_addedItems.Count > 0)
      {
        var item = _addedItems.Pop();
        var content = item.GetContent (out var wasAlreadyOpen, this);
        if (content == null)
          continue;

        var headers = HeaderFinder.GetHeaderDefinitionForItem (item);
        if (headers == null)
          continue;

        var fileName = item.FileNames[1];
        var properties = item.GetAdditionalProperties() as List<AdditionalProperty>;
        var dialog = new WpfFeaturesQuestion(fileName);
        var dialogResult = dialog.ShowDialog();

        if (dialogResult.HasValue && dialogResult.Value)
        {
          properties.Add(new AdditionalProperty(
            "%Features%",
            dialog.Features
          ));
        }

        var result = await HeaderReplacer.RemoveOrReplaceHeader (
            new HeaderContentInput (content, fileName, headers, properties));
        await CoreHelpers.HandleResultAsync (result, this, wasAlreadyOpen, false);
      }

      _currentCommandEvents.AfterExecute -= FinishedAddingItem;
    }

    private void CreateAndConfigureFileAppender (string solutionName)
    {
      var logPath = OptionsFacade.DefaultLogPath;

      _fileAppender?.Close();
      _fileAppender = new FileAppender
                      {
                          Threshold = Level.Debug,
                          AppendToFile = true,
                          File = Path.Combine (logPath, $"HeaderManager_{solutionName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"),
                          Layout = new PatternLayout ("%date [%-5level] %logger: %message%newline")
                      };

      _fileAppender.ActivateOptions();
      BasicConfigurator.Configure (_fileAppender);
    }

    private async Task CreateAndConfigureOutputPaneAppenderAsync ()
    {
      await JoinableTaskFactory.SwitchToMainThreadAsync();
      _outputPane = await GetServiceAsync (typeof (SVsOutputWindow)) as IVsOutputWindow;
      Assumes.Present (_outputPane);

      if (_outputPane == null)
      {
        s_log.Error ("Unable to add output pane log appender (output pane not available)");
        return;
      }

      _outputPane.CreatePane (ref GuidOutputPaneAppender, "HeaderManager", 1, 1);
      _outputPaneAppender = new OutputPaneAppender (_outputPane, Level.Info);
      _outputPaneAppender.ActivateOptions();

      BasicConfigurator.Configure (_outputPaneAppender);
    }

    private async Task MigrateOptionsAsync ()
    {
      if (!File.Exists (OptionsFacade.DefaultCoreOptionsPath) || !File.Exists (OptionsFacade.DefaultVisualStudioOptionsPath))
      {
        var optionsPage = (OptionsPage) GetDialogPage (typeof (OptionsPage));
        var defaultHeaderPage = (DefaultHeaderPage) GetDialogPage (typeof (DefaultHeaderPage));
        var languagesPage = (LanguagesPage) GetDialogPage (typeof (LanguagesPage));

        optionsPage.MigrateOptions();
        defaultHeaderPage.MigrateOptions();
        languagesPage.MigrateOptions();
      }
      else
      {
        OptionsFacade.CurrentOptions = await OptionsFacade.LoadAsync();
      }

      OptionsFacade.CurrentOptions.Version = Version;
      await OptionsFacade.SaveAsync (OptionsFacade.CurrentOptions);
    }
  }
}
