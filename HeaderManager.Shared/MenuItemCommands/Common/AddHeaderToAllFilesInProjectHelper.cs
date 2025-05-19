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
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using HeaderManager.Core;
using HeaderManager.Headers;
using HeaderManager.Interfaces;
using HeaderManager.ResultObjects;
using HeaderManager.UpdateViewModels;
using HeaderManager.Utils;

namespace HeaderManager.MenuItemCommands.Common
{
  internal class AddHeaderToAllFilesInProjectHelper
  {
    private readonly BaseUpdateViewModel _baseUpdateViewModel;
    private readonly CancellationToken _cancellationToken;
    private readonly IHeaderExtension _licenseHeaderExtension;

    public AddHeaderToAllFilesInProjectHelper (
        CancellationToken cancellationToken,
        IHeaderExtension licenseHeaderExtension,
        BaseUpdateViewModel baseUpdateViewModel)
    {
      _cancellationToken = cancellationToken;
      _licenseHeaderExtension = licenseHeaderExtension;
      _baseUpdateViewModel = baseUpdateViewModel;
    }

    public async Task<AddHeaderToAllFilesResult> RemoveOrReplaceHeadersAsync (object projectOrProjectItem)
    {
      await _licenseHeaderExtension.JoinableTaskFactory.SwitchToMainThreadAsync();
      var project = projectOrProjectItem as Project;
      var projectItem = projectOrProjectItem as ProjectItem;
      var replacerInput = new List<HeaderContentInput>();

      var countSubHeadersFound = 0;
      IDictionary<string, string[]> headers;
      var linkedItems = new List<ProjectItem>();

      if (project == null && projectItem == null)
        return new AddHeaderToAllFilesResult (countSubHeadersFound, true, linkedItems);

      ProjectItems projectItems;
      var fileOpenedStatus = new Dictionary<string, bool>();
      if (project != null)
      {
        headers = HeaderFinder.GetHeaderDefinitionForProjectWithFallback (project);
        projectItems = project.ProjectItems;
      }
      else
      {
        headers = HeaderFinder.GetHeaderDefinitionForItem (projectItem);
        projectItems = projectItem.ProjectItems;
      }

      foreach (ProjectItem item in projectItems)
        if (ProjectItemInspection.IsPhysicalFile (item) && ProjectItemInspection.IsLink (item))
        {
          linkedItems.Add (item);
        }
        else
        {
          var inputFiles = CoreHelpers.GetFilesToProcess (item, headers, out var subHeaders, out var subFileOpenedStatus);
          replacerInput.AddRange (inputFiles);
          foreach (var status in subFileOpenedStatus)
            fileOpenedStatus[status.Key] = status.Value;

          countSubHeadersFound = subHeaders;
        }

      var result = await _licenseHeaderExtension.HeaderReplacer.RemoveOrReplaceHeader (
          replacerInput,
          CoreHelpers.CreateProgress (_baseUpdateViewModel, project?.Name, fileOpenedStatus, _cancellationToken),
          _cancellationToken);
      await CoreHelpers.HandleResultAsync (result, _licenseHeaderExtension, _baseUpdateViewModel, project?.Name, fileOpenedStatus, _cancellationToken);

      return new AddHeaderToAllFilesResult (countSubHeadersFound, headers == null, linkedItems);
    }
  }
}
