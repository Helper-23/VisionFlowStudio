using System.Collections.ObjectModel;

namespace VisionFlowStudio.App
{
    public sealed class ProjectTreeNodeViewModel
    {
        public string Header { get; set; }
        public string Kind { get; set; }
        public object Model { get; set; }
        public bool IsExpanded { get; set; } = true;
        public ObservableCollection<ProjectTreeNodeViewModel> Children { get; private set; } =
            new ObservableCollection<ProjectTreeNodeViewModel>();
    }
}
