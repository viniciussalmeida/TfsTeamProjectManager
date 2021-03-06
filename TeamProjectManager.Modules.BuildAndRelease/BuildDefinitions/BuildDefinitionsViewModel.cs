﻿using Microsoft.Practices.Prism.Events;
using Microsoft.TeamFoundation.Build.WebApi;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TeamProjectManager.Common.Events;
using TeamProjectManager.Common.Infrastructure;
using TeamProjectManager.Common.ObjectModel;

namespace TeamProjectManager.Modules.BuildAndRelease.BuildDefinitions
{
    [Export]
    public class BuildDefinitionsViewModel : ViewModelBase
    {
        #region Properties

        public AsyncRelayCommand GetBuildDefinitionsCommand { get; private set; }
        public AsyncRelayCommand DeleteSelectedBuildDefinitionsCommand { get; private set; }

        #endregion

        #region Observable Properties

        public ICollection<BuildDefinition> BuildDefinitions
        {
            get { return this.GetValue(BuildDefinitionsProperty); }
            set { this.SetValue(BuildDefinitionsProperty, value); }
        }

        public static readonly ObservableProperty<ICollection<BuildDefinition>> BuildDefinitionsProperty = new ObservableProperty<ICollection<BuildDefinition>, BuildDefinitionsViewModel>(o => o.BuildDefinitions);

        public ICollection<BuildDefinition> SelectedBuildDefinitions
        {
            get { return this.GetValue(SelectedBuildDefinitionsProperty); }
            set { this.SetValue(SelectedBuildDefinitionsProperty, value); }
        }

        public static readonly ObservableProperty<ICollection<BuildDefinition>> SelectedBuildDefinitionsProperty = new ObservableProperty<ICollection<BuildDefinition>, BuildDefinitionsViewModel>(o => o.SelectedBuildDefinitions);

        #endregion

        #region Constructors

        [ImportingConstructor]
        protected BuildDefinitionsViewModel(IEventAggregator eventAggregator, ILogger logger)
            : base(eventAggregator, logger, "Allows you to manage build definitions for Team Projects.")
        {
            this.GetBuildDefinitionsCommand = new AsyncRelayCommand(GetBuildDefinitions, CanGetBuildDefinitions);
            this.DeleteSelectedBuildDefinitionsCommand = new AsyncRelayCommand(DeleteSelectedBuildDefinitions, CanDeleteSelectedBuildDefinitions);
        }

        #endregion

        #region GetBuildDefinitions Command

        private bool CanGetBuildDefinitions(object argument)
        {
            return IsAnyTeamProjectSelected();
        }

        private async Task GetBuildDefinitions(object argument)
        {
            var teamProjects = this.SelectedTeamProjects.ToList();
            var task = new ApplicationTask("Retrieving build definitions", teamProjects.Count, true);
            PublishStatus(new StatusEventArgs(task));

            try
            {
                var tfs = GetSelectedTfsTeamProjectCollection();
                var buildServer = tfs.GetClient<BuildHttpClient>();

                var step = 0;
                var buildDefinitions = new List<BuildDefinition>();
                foreach (var teamProject in teamProjects)
                {
                    task.SetProgress(step++, string.Format(CultureInfo.CurrentCulture, "Processing Team Project \"{0}\"", teamProject.Name));
                    try
                    {
                        var projectBuildDefinitions = await buildServer.GetFullDefinitionsAsync(project: teamProject.Name);//.ConfigureAwait(false);
                        buildDefinitions.AddRange(projectBuildDefinitions);
                    }
                    catch (Exception exc)
                    {
                        task.SetWarning(string.Format(CultureInfo.CurrentCulture, "An error occurred while processing Team Project \"{0}\"", teamProject.Name), exc);
                    }
                    if (task.IsCanceled)
                    {
                        task.Status = "Canceled";
                        break;
                    }
                }
                this.BuildDefinitions = buildDefinitions;
                task.SetComplete("Retrieved " + this.BuildDefinitions.Count.ToCountString("build definition"));
            }
            catch (Exception exc)
            {
                Logger.Log("An unexpected exception occurred while retrieving build definitions", exc);
                task.SetError(exc);
                task.SetComplete("An unexpected exception occurred");
            }
        }

        #endregion

        #region DeleteSelectedBuildDefinitions Command

        private bool CanDeleteSelectedBuildDefinitions(object argument)
        {
            return this.SelectedBuildDefinitions != null && this.SelectedBuildDefinitions.Count > 0;
        }

        private async Task DeleteSelectedBuildDefinitions(object argument)
        {
            var result = MessageBox.Show("This will delete the selected build definitions. Are you sure you want to continue?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var buildDefinitionsToDelete = this.SelectedBuildDefinitions;
            var task = new ApplicationTask("Deleting build definitions", buildDefinitionsToDelete.Count, true);
            PublishStatus(new StatusEventArgs(task));
            try
            {
                var tfs = GetSelectedTfsTeamProjectCollection();
                var buildServer = tfs.GetClient<BuildHttpClient>();

                var step = 0;
                var count = 0;
                foreach (var buildDefinitionToDelete in buildDefinitionsToDelete)
                {
                    task.SetProgress(step++, string.Format(CultureInfo.CurrentCulture, "Deleting build definition \"{0}\" in Team Project \"{1}\"", buildDefinitionToDelete.Name, buildDefinitionToDelete.Project.Name));
                    try
                    {
                        // Delete the build definitions one by one to avoid one failure preventing the other build definitions from being deleted.
                        await buildServer.DeleteDefinitionAsync(buildDefinitionToDelete.Project.Id, buildDefinitionToDelete.Id);
                        count++;
                    }
                    catch (Exception exc)
                    {
                        task.SetWarning(string.Format(CultureInfo.CurrentCulture, "An error occurred while deleting build definition \"{0}\" in Team Project \"{1}\"", buildDefinitionToDelete.Name, buildDefinitionToDelete.Project.Name), exc);
                    }
                    if (task.IsCanceled)
                    {
                        task.Status = "Canceled";
                        break;
                    }
                }
                task.SetComplete("Deleted " + count.ToCountString("build definition"));

                // Refresh the list.
                await GetBuildDefinitions(null);
            }
            catch (Exception exc)
            {
                Logger.Log("An unexpected exception occurred while deleting build definitions", exc);
                task.SetError(exc);
                task.SetComplete("An unexpected exception occurred");
            }
        }

        #endregion
    }
}