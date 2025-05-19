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
using System.Linq;
using System.Threading;
using EnvDTE;
using HeaderManager.Headers;
using HeaderManager.Interfaces;
using HeaderManager.MenuItemCommands.SolutionMenu;
using HeaderManager.ResultObjects;
using HeaderManager.UpdateViewModels;
using HeaderManager.Utils;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace HeaderManager.MenuItemCommands.Common
{
  internal static class FolderProjectMenuHelper
  {
    public static void AddExistingHeaderDefinitionFile (IHeaderExtension serviceProvider)
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var project = serviceProvider.GetSolutionExplorerItem() as Project;
      var projectItem = serviceProvider.GetSolutionExplorerItem() as ProjectItem;

      string fileName;
      if (project != null)
        fileName = project.FileName;
      else if (projectItem != null)
        fileName = projectItem.Name;
      else
        return;

      ProjectItems projectItems = null;
      if (project != null)
        projectItems = project.ProjectItems;
      else if (projectItem != null)
        projectItems = projectItem.ProjectItems;

      ExistingHeaderDefinitionFileAdder.AddDefinitionFileToOneProject (fileName, projectItems);
    }

    public static async Task AddHeaderToAllFilesAsync (
        CancellationToken cancellationToken,
        IHeaderExtension serviceProvider,
        BaseUpdateViewModel folderProjectUpdateViewModel)
    {
      await serviceProvider.JoinableTaskFactory.SwitchToMainThreadAsync();
      var obj = serviceProvider.GetSolutionExplorerItem();
      var addHeaderToAllFilesCommand = new AddHeaderToAllFilesInProjectHelper (
          cancellationToken,
          serviceProvider,
          folderProjectUpdateViewModel);

      var addHeaderToAllFilesResult = await addHeaderToAllFilesCommand.RemoveOrReplaceHeadersAsync (obj);

      await HandleLinkedFilesAndShowMessageBoxAsync (serviceProvider, addHeaderToAllFilesResult.LinkedItems);
      await HandleAddHeaderToAllFilesInProjectResultAsync (
          cancellationToken,
          serviceProvider,
          obj,
          addHeaderToAllFilesResult,
          folderProjectUpdateViewModel);
    }

    public static void AddNewHeaderDefinitionFile (IHeaderExtension serviceProvider)
    {
      ThreadHelper.ThrowIfNotOnUIThread();

      var page = serviceProvider.DefaultHeaderPageModel;
      var solutionItem = serviceProvider.GetSolutionExplorerItem();
      var project = solutionItem as Project;
      if (project == null)
        if (solutionItem is ProjectItem projectItem)
          HeaderDefinitionFileHelper.AddHeaderDefinitionFile (projectItem, page);

      if (project == null)
        return;

      var licenseHeaderDefinitionFile = HeaderDefinitionFileHelper.AddHeaderDefinitionFile (project, page);
      licenseHeaderDefinitionFile.Open (Constants.vsViewKindCode).Activate();
    }

    private static async Task HandleAddHeaderToAllFilesInProjectResultAsync (
        CancellationToken cancellationToken,
        IHeaderExtension serviceProvider,
        object obj,
        AddHeaderToAllFilesResult addResult,
        BaseUpdateViewModel baseUpdateViewModel)
    {
      await serviceProvider.JoinableTaskFactory.SwitchToMainThreadAsync();
      var project = obj as Project;
      var projectItem = obj as ProjectItem;
      if (project == null && projectItem == null)
        return;
      var currentProject = project;

      if (projectItem != null)
        currentProject = projectItem.ContainingProject;

      if (addResult.NoHeaderFound)
      {
        // No license header found...
        var solutionSearcher = new AllSolutionProjectsSearcher();
        var projects = solutionSearcher.GetAllProjects (serviceProvider.Dte2.Solution);

        if (projects.Any (projectInSolution => HeaderFinder.GetHeaderDefinitionForProjectWithoutFallback (projectInSolution) != null))
        {
          baseUpdateViewModel.ProcessedFilesCountCurrentProject = 0;
          // If another project has a license header, offer to add a link to the existing one.
          if (MessageBoxHelper.AskYesNo (Resources.Question_AddExistingDefinitionFileToProject.ReplaceNewLines()))
          {
            ExistingHeaderDefinitionFileAdder.AddDefinitionFileToOneProject (currentProject.FileName, currentProject.ProjectItems);
            await AddHeaderToAllFilesAsync (cancellationToken, serviceProvider, baseUpdateViewModel);
          }
        }
        else
        {
          // If no project has a license header, offer to add one for the solution.
          if (MessageBoxHelper.AskYesNo (Resources.Question_AddNewHeaderDefinitionForSolution.ReplaceNewLines()))
            AddNewSolutionHeaderDefinitionFileCommand.Instance.Invoke (serviceProvider.Dte2.Solution);
        }
      }
    }

    private static async Task HandleLinkedFilesAndShowMessageBoxAsync (IHeaderExtension serviceProvider, IEnumerable<ProjectItem> linkedItems)
    {
      ILinkedFileFilter linkedFileFilter = new LinkedFileFilter (serviceProvider.Dte2.Solution);
      linkedFileFilter.Filter (linkedItems);

      var linkedFileHandler = new LinkedFileHandler (serviceProvider);
      await linkedFileHandler.HandleAsync (linkedFileFilter);

      if (linkedFileHandler.Message != string.Empty)
        MessageBoxHelper.ShowMessage (linkedFileHandler.Message);
    }
  }
}
