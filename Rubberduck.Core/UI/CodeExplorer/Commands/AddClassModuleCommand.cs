using System.Collections.Generic;
using Rubberduck.VBEditor.ComManagement;
using Rubberduck.VBEditor.Events;
using Rubberduck.VBEditor.SafeComWrappers;

namespace Rubberduck.UI.CodeExplorer.Commands
{
    public class AddClassModuleCommand : AddComponentCommandBase
    {
        public AddClassModuleCommand(
            ICodeExplorerAddComponentService addComponentService, 
            IVbeEvents vbeEvents,
            IProjectsProvider projectsProvider) 
            : base(addComponentService, vbeEvents, projectsProvider)
        {}

        public override IEnumerable<ProjectType> AllowableProjectTypes => ProjectTypes.All;

        public override ComponentType ComponentType => ComponentType.ClassModule;
    }
}
