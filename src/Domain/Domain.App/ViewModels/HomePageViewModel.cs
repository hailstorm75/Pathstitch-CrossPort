using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

[NavigationPage(NavigationAddressBook.HomePage)]
public sealed partial class HomePageViewModel(
    ILogger<HomePageViewModel> logger,
    ProjectSessionService projectSessionService,
    IGeometryKernelDescriptorProvider geometryKernelDescriptorProvider) : BasePageViewModel(logger)
{
    private readonly GeometryKernelDescriptor _geometryKernel = geometryKernelDescriptorProvider.Current;
    private ProjectTemplateDefinition _selectedTemplate = new(
        TemplateId: "blank-project",
        DisplayName: "Blank project",
        DefaultProjectName: "Untitled Project",
        Description: "A clean starter project with no preset content.");

    private string _projectName = "Untitled Project";
    private ProjectSession? _currentSession;
    private IReadOnlyList<RecentProjectSummary> _recentProjects = [];
    private RecentProjectSummary? _selectedRecentProject;
    private string _homeStatusText = "Drop a Pathstitch project or 3D model to continue.";
    private bool _isLoading;

    public IReadOnlyList<ProjectTemplateDefinition> AvailableTemplates => projectSessionService.AvailableTemplates;

    public ProjectTemplateDefinition SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (!SetProperty(ref _selectedTemplate, value))
                return;

            if (string.IsNullOrWhiteSpace(ProjectName) || ProjectName == value.DefaultProjectName)
                ProjectName = value.DefaultProjectName;
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public ProjectSession? CurrentSession
    {
        get => _currentSession;
        private set => SetProperty(ref _currentSession, value);
    }

    public IReadOnlyList<RecentProjectSummary> RecentProjects
    {
        get => _recentProjects;
        private set
        {
            if (!SetProperty(ref _recentProjects, value))
                return;

            OnPropertyChanged(nameof(HasRecentProjects));
        }
    }

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public bool HasNoRecentProjects => !HasRecentProjects;

    public RecentProjectSummary? SelectedRecentProject
    {
        get => _selectedRecentProject;
        private set
        {
            if (!SetProperty(ref _selectedRecentProject, value))
                return;

            OnPropertyChanged(nameof(HasSelectedRecentProject));
        }
    }

    public bool HasSelectedRecentProject => SelectedRecentProject is not null;

    public string GeometryKernelDisplayName => _geometryKernel.DisplayName;

    public string GeometryKernelImplementationName => _geometryKernel.ImplementationName;

    public string GeometryKernelRuntimeSummary => _geometryKernel.RuntimeSummary;

    public string GeometryKernelCapabilitySummary => _geometryKernel.CapabilitySummary;

    public string GeometryKernelRequirementSummary => _geometryKernel.RequirementSummary;

    public string GeometryKernelSupportedSourceModelSummary => _geometryKernel.SupportedSourceModelSummary;

    public string HomeStatusText
    {
        get => _homeStatusText;
        private set => SetProperty(ref _homeStatusText, value);
    }

    public override bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    protected override ValueTask LoadPageAsync(CancellationToken token)
    {
        SelectedTemplate = projectSessionService.DefaultTemplate;
        ProjectName = SelectedTemplate.DefaultProjectName;
        CurrentSession = projectSessionService.CurrentSession;
        RefreshRecentProjects();
        HomeStatusText = "Drop a Pathstitch project or 3D model to continue.";
        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task CreateTemplateProject()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var session = await projectSessionService
                .CreateTemplateProjectAsync(ProjectName, SelectedTemplate)
                .ConfigureAwait(true);

            if (session is not null)
            {
                RefreshRecentProjects();
                StartEditorSession(new ProjectLaunchRequest(session, []));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenTemplateProject()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var session = await projectSessionService
                .OpenTemplateProjectAsync()
                .ConfigureAwait(true);

            if (session is not null)
            {
                RefreshRecentProjects();
                StartEditorSession(new ProjectLaunchRequest(session, []));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenWorkspaceFiles()
    {
        await OpenWorkspaceFilesCoreAsync(
            filePaths: null,
            useFileDialog: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenRecentProject(RecentProjectSummary? recentProject)
    {
        if (IsLoading || recentProject is null || !recentProject.IsAvailable)
            return;

        IsLoading = true;
        try
        {
            var session = await projectSessionService
                .OpenRecentProjectAsync(recentProject.ProjectFilePath)
                .ConfigureAwait(true);

            RefreshRecentProjects();

            if (session is not null)
                StartEditorSession(new ProjectLaunchRequest(session, []));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RemoveRecentProject(RecentProjectSummary? recentProject)
    {
        if (recentProject is null)
            return;

        projectSessionService.RemoveRecentProject(recentProject.ProjectFilePath);
        RefreshRecentProjects();
    }

    [RelayCommand]
    private void SelectRecentProject(RecentProjectSummary? recentProject)
    {
        if (recentProject is null)
            return;

        SelectedRecentProject = recentProject;
        HomeStatusText = recentProject.IsAvailable
            ? $"Selected recent project: {recentProject.ProjectName}"
            : $"Recent project missing: {recentProject.ProjectName}";
        RefreshRecentProjects();
    }

    public Task OpenSelectedRecentProjectAsync()
        => OpenRecentProject(SelectedRecentProject);

    public void SelectRecentProjectCard(RecentProjectSummary? recentProject)
        => SelectRecentProject(recentProject);

    public Task OpenRecentProjectCardAsync(RecentProjectSummary? recentProject)
        => OpenRecentProject(recentProject);

    public void SelectNextRecentProject()
    {
        if (RecentProjects.Count == 0)
            return;

        if (SelectedRecentProject is null)
        {
            SelectRecentProject(RecentProjects[0]);
            return;
        }

        var currentIndex = RecentProjects
            .ToList()
            .FindIndex(project => string.Equals(
                project.ProjectFilePath,
                SelectedRecentProject.ProjectFilePath,
                StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
        {
            SelectRecentProject(RecentProjects[0]);
            return;
        }

        SelectRecentProject(RecentProjects[Math.Min(currentIndex + 1, RecentProjects.Count - 1)]);
    }

    public void SelectPreviousRecentProject()
    {
        if (RecentProjects.Count == 0)
            return;

        if (SelectedRecentProject is null)
        {
            SelectRecentProject(RecentProjects[^1]);
            return;
        }

        var currentIndex = RecentProjects
            .ToList()
            .FindIndex(project => string.Equals(
                project.ProjectFilePath,
                SelectedRecentProject.ProjectFilePath,
                StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
        {
            SelectRecentProject(RecentProjects[^1]);
            return;
        }

        SelectRecentProject(RecentProjects[Math.Max(currentIndex - 1, 0)]);
    }

    private void RefreshRecentProjects()
    {
        var recentProjects = projectSessionService.RecentProjects;
        var selectedProjectPath = SelectedRecentProject?.ProjectFilePath;

        SelectedRecentProject = selectedProjectPath is null
            ? null
            : recentProjects.FirstOrDefault(project =>
                string.Equals(project.ProjectFilePath, selectedProjectPath, StringComparison.OrdinalIgnoreCase));

        RecentProjects = recentProjects
            .Select(project => project with
            {
                IsSelected = SelectedRecentProject is not null
                    && string.Equals(project.ProjectFilePath, SelectedRecentProject.ProjectFilePath, StringComparison.OrdinalIgnoreCase),
            })
            .ToArray();
    }

    public async Task OpenFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        await OpenWorkspaceFilesCoreAsync(filePaths, useFileDialog: false, cancellationToken).ConfigureAwait(true);
    }

    public Task OpenFilesAsync(string[] filePaths)
    {
        return OpenFilesAsync((IReadOnlyList<string>)filePaths);
    }

    private async Task OpenWorkspaceFilesCoreAsync(
        IReadOnlyList<string>? filePaths,
        bool useFileDialog,
        CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            ProjectLaunchRequest? launchRequest;
            try
            {
                launchRequest = useFileDialog
                    ? await projectSessionService.OpenWorkspaceFilesAsync(cancellationToken).ConfigureAwait(true)
                    : await projectSessionService.OpenWorkspaceFilesAsync(filePaths ?? [], cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                HomeStatusText = ex.Message;
                return;
            }

            RefreshRecentProjects();

            if (launchRequest is null)
                return;

            HomeStatusText = launchRequest.PendingSourceModelPaths.Count switch
            {
                > 0 when launchRequest.Session.Origin == ProjectSessionOrigin.Imported
                    => $"Opening {launchRequest.PendingSourceModelPaths.Count} 3D model(s) in a fresh workspace.",
                > 0
                    => $"Opening {launchRequest.Session.ProjectName} and importing {launchRequest.PendingSourceModelPaths.Count} 3D model(s).",
                _ => $"Opening {launchRequest.Session.ProjectName}.",
            };

            StartEditorSession(launchRequest);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void StartEditorSession(ProjectLaunchRequest launchRequest)
    {
        var session = launchRequest.Session;
        CurrentSession = session;

        var parameters = new Dictionary<string, object>
        {
            [EditorNavigationParameterKeys.ProjectSession] = session,
        };

        if (launchRequest.PendingSourceModelPaths.Count > 0)
            parameters[EditorNavigationParameterKeys.PendingSourceModelPaths] = launchRequest.PendingSourceModelPaths;

        WeakReferenceMessenger.Default.Send(new NavigationChangeRequestMessage(
            NavigationAddressBook.EditorPage,
            parameters));
    }
}
