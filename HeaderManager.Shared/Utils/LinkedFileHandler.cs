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
using System.Linq;
using HeaderManager.Core;
using HeaderManager.Headers;
using HeaderManager.Interfaces;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace HeaderManager.Utils
{
  public class LinkedFileHandler
  {
    private readonly IHeaderExtension _licenseHeaderExtension;

    public LinkedFileHandler (IHeaderExtension licenseHeaderExtension)
    {
      _licenseHeaderExtension = licenseHeaderExtension;
      Message = string.Empty;
    }

    public string Message { get; private set; }

    /// <summary>
    ///   Applies the license headers on the linked files in the linked file filter.
    /// </summary>
    /// <param name="linkedFileFilter">Specifies the linked file filter that holds the project items.</param>
    /// <returns></returns>
    public async Task HandleAsync (ILinkedFileFilter linkedFileFilter)
    {
      await HeadersPackage.Instance.JoinableTaskFactory.SwitchToMainThreadAsync();

      foreach (var projectItem in linkedFileFilter.ToBeProgressed)
      {
        var content = projectItem.GetContent (out var wasAlreadyOpen, _licenseHeaderExtension);
        if (content == null)
          continue;

        var headers = HeaderFinder.GetHeaderDefinitionForItem (projectItem);
        var result = await _licenseHeaderExtension.HeaderReplacer.RemoveOrReplaceHeader (
            new HeaderContentInput (content, projectItem.FileNames[1], headers, projectItem.GetAdditionalProperties()));
        await CoreHelpers.HandleResultAsync (result, _licenseHeaderExtension, wasAlreadyOpen, true);
      }

      if (linkedFileFilter.NoHeaderFile.Any() || linkedFileFilter.NotInSolution.Any())
      {
        var notProgressedItems = linkedFileFilter.NoHeaderFile.Concat (linkedFileFilter.NotInSolution).ToList();
        var notProgressedNames = notProgressedItems.Select (
            x =>
            {
              ThreadHelper.ThrowIfNotOnUIThread();
              return x.Name;
            });

        Message += string.Format (Resources.LinkedFileUpdateInformation, string.Join ("\n", notProgressedNames)).ReplaceNewLines();
      }
    }
  }
}
