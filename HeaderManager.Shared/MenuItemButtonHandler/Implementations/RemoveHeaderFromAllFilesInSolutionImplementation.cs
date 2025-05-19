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
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using HeaderManager.Interfaces;
using HeaderManager.MenuItemCommands.Common;
using HeaderManager.UpdateViewModels;
using HeaderManager.Utils;
using Window = System.Windows.Window;

namespace HeaderManager.MenuItemButtonHandler.Implementations
{
  public class RemoveHeaderFromAllFilesInSolutionImplementation : MenuItemButtonHandlerImplementation
  {
    private const string c_commandName = "Remove Header from all files in Solution";
    private readonly IHeaderExtension _licenseHeaderExtension;

    public RemoveHeaderFromAllFilesInSolutionImplementation (IHeaderExtension licenseHeaderExtension)
    {
      _licenseHeaderExtension = licenseHeaderExtension;
    }

    public override string Description => c_commandName;

    public override Task DoWorkAsync (CancellationToken cancellationToken, BaseUpdateViewModel viewModel, Solution solution, Window window)
    {
      return DoWorkAsync (cancellationToken, viewModel, solution);
    }

    public override async Task DoWorkAsync (CancellationToken cancellationToken, BaseUpdateViewModel viewModel, Solution solution)
    {
      if (solution == null)
        return;

      if (!(viewModel is SolutionUpdateViewModel updateViewModel))
        throw new ArgumentException ($"Argument {nameof(viewModel)} must be of type {nameof(SolutionUpdateViewModel)}");

      await _licenseHeaderExtension.JoinableTaskFactory.SwitchToMainThreadAsync();
      var allSolutionProjectsSearcher = new AllSolutionProjectsSearcher();
      var projectsInSolution = allSolutionProjectsSearcher.GetAllProjects (solution);

      updateViewModel.ProcessedProjectCount = 0;
      updateViewModel.ProjectCount = projectsInSolution.Count;
      var removeAllHeadersCommand = new RemoveHeaderFromAllFilesInProjectHelper (cancellationToken, _licenseHeaderExtension, updateViewModel);

      foreach (var project in projectsInSolution)
      {
        await removeAllHeadersCommand.ExecuteAsync (project);
        await IncrementProjectCountAsync (updateViewModel).ConfigureAwait (true);
      }
    }

    public override Task DoWorkAsync (CancellationToken cancellationToken, BaseUpdateViewModel viewModel)
    {
      throw new NotSupportedException (UnsupportedOverload);
    }

    private async Task IncrementProjectCountAsync (BaseUpdateViewModel viewModel)
    {
      await _licenseHeaderExtension.JoinableTaskFactory.SwitchToMainThreadAsync();
      viewModel.ProcessedProjectCount++;
    }
  }
}
