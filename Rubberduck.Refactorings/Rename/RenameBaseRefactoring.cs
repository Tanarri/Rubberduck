﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rubberduck.Parsing;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing.Rewriter;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using Rubberduck.VBEditor.ComManagement;
using Rubberduck.VBEditor.SafeComWrappers;

namespace Rubberduck.Refactorings.Rename
{
    public class RenameBaseRefactoring : BaseRefactoringWithSuspensionBase<RenameModel>
    {
        private const string AppendUnderscoreFormat = "{0}_";
        private const string PrependUnderscoreFormat = "_{0}";

        private readonly IDeclarationFinderProvider _declarationFinderProvider;
        private readonly IProjectsProvider _projectsProvider;
        private readonly IDictionary<DeclarationType, Action<RenameModel, IRewriteSession>> _renameActions;

        public RenameBaseRefactoring(
            IDeclarationFinderProvider declarationFinderProvider,
            IProjectsProvider projectsProvider,
            IParseManager parserManager,
            IRewritingManager rewritingManager)
            : base(parserManager, rewritingManager)
        {
            _declarationFinderProvider = declarationFinderProvider;
            _projectsProvider = projectsProvider;

            _renameActions = new Dictionary<DeclarationType, Action<RenameModel, IRewriteSession>>
            {
                {DeclarationType.Member, RenameMember},
                {DeclarationType.Parameter, RenameParameter},
                {DeclarationType.Event, RenameEvent},
                {DeclarationType.Variable, RenameVariable},
                {DeclarationType.Module, RenameModule},
                {DeclarationType.Project, RenameProject}
            };
        }

        protected override bool RequiresSuspension(RenameModel model)
        {
            //The parser needs to be suspended during the refactoring of a component because the VBE API object rename causes a separate reparse. 
            var targetType = model.Target.DeclarationType;
            return targetType.HasFlag(DeclarationType.Module)
                   || targetType.HasFlag(DeclarationType.Project);
        }

        protected override void Refactor(RenameModel model, IRewriteSession rewriteSession)
        {
            Debug.Assert(!model.NewName.Equals(model.Target.IdentifierName, StringComparison.InvariantCultureIgnoreCase),
                $"input validation fail: New Name equals Original Name ({model.Target.IdentifierName})");

            var actionKeys = _renameActions.Keys.Where(decType => model.Target.DeclarationType.HasFlag(decType)).ToList();
            if (actionKeys.Any())
            {
                Debug.Assert(actionKeys.Count == 1, $"{actionKeys.Count} Rename Actions have flag '{model.Target.DeclarationType.ToString()}'");
                _renameActions[actionKeys.FirstOrDefault()](model, rewriteSession);
            }
            else
            {
                RenameStandardElements(model.Target, model.NewName, rewriteSession);
            }
        }

        private void RenameMember(RenameModel model, IRewriteSession rewriteSession)
        {
            if (model.Target.DeclarationType.HasFlag(DeclarationType.Property))
            {
                var members = _declarationFinderProvider.DeclarationFinder.MatchName(model.Target.IdentifierName)
                    .Where(item => item.ProjectId == model.Target.ProjectId
                        && item.ComponentName == model.Target.ComponentName
                        && item.DeclarationType.HasFlag(DeclarationType.Property));

                foreach (var member in members)
                {
                    RenameStandardElements(member, model.NewName, rewriteSession);
                }
            }
            else
            {
                RenameStandardElements(model.Target, model.NewName, rewriteSession);
            }

            if (!model.IsInterfaceMemberRename)
            {
                return;
            }

            var implementations = _declarationFinderProvider.DeclarationFinder.FindAllInterfaceImplementingMembers()
                .Where(impl => ReferenceEquals(model.Target.ParentDeclaration, impl.InterfaceImplemented)
                               && impl.InterfaceMemberImplemented.IdentifierName.Equals(model.Target.IdentifierName));

            RenameDefinedFormatMembers(model, implementations.ToList(), PrependUnderscoreFormat, rewriteSession);
        }

        private void RenameParameter(RenameModel model, IRewriteSession rewriteSession)
        {
            if (model.Target.ParentDeclaration.DeclarationType.HasFlag(DeclarationType.Property))
            {
                var parameters = _declarationFinderProvider.DeclarationFinder.MatchName(model.Target.IdentifierName).Where(param =>
                   param.ParentDeclaration.DeclarationType.HasFlag(DeclarationType.Property)
                   && param.DeclarationType == DeclarationType.Parameter
                    && param.ParentDeclaration.IdentifierName.Equals(model.Target.ParentDeclaration.IdentifierName)
                    && param.ParentDeclaration.ParentScopeDeclaration.Equals(model.Target.ParentDeclaration.ParentScopeDeclaration));

                foreach (var param in parameters)
                {
                    RenameStandardElements(param, model.NewName, rewriteSession);
                }
            }
            else
            {
                RenameStandardElements(model.Target, model.NewName, rewriteSession);
            }
        }

        private void RenameEvent(RenameModel model, IRewriteSession rewriteSession)
        {
            RenameStandardElements(model.Target, model.NewName, rewriteSession);

            var withEventsDeclarations = _declarationFinderProvider.DeclarationFinder.UserDeclarations(DeclarationType.Variable)
                .Where(varDec => varDec.IsWithEvents && varDec.AsTypeName.Equals(model.Target.ParentDeclaration.IdentifierName));

            var eventHandlers = withEventsDeclarations.SelectMany(we => _declarationFinderProvider.DeclarationFinder.FindHandlersForWithEventsField(we));
            RenameDefinedFormatMembers(model, eventHandlers.ToList(), PrependUnderscoreFormat, rewriteSession);
        }

        private void RenameVariable(RenameModel model, IRewriteSession rewriteSession)
        {
            if ((model.Target.Accessibility == Accessibility.Public ||
                 model.Target.Accessibility == Accessibility.Implicit)
                && model.Target.ParentDeclaration is ClassModuleDeclaration classDeclaration
                && classDeclaration.Subtypes.Any())
            {
                RenameMember(model, rewriteSession);
            }
            else if (model.Target.DeclarationType.HasFlag(DeclarationType.Control))
            {
                var component = _projectsProvider.Component(model.Target.QualifiedName.QualifiedModuleName);
                using (var controls = component.Controls)
                {
                    using (var control = controls.SingleOrDefault(item => item.Name == model.Target.IdentifierName))
                    {
                        Debug.Assert(control != null,
                            $"input validation fail: unable to locate '{model.Target.IdentifierName}' in Controls collection");

                        control.Name = model.NewName;
                    }
                }
                RenameReferences(model.Target, model.NewName, rewriteSession);
                var controlEventHandlers = FindEventHandlersForControl(model.Target);
                RenameDefinedFormatMembers(model, controlEventHandlers.ToList(), AppendUnderscoreFormat, rewriteSession);
            }
            else
            {
                RenameStandardElements(model.Target, model.NewName, rewriteSession);
                if (model.Target.IsWithEvents)
                {
                    var eventHandlers = _declarationFinderProvider.DeclarationFinder.FindHandlersForWithEventsField(model.Target);
                    RenameDefinedFormatMembers(model, eventHandlers.ToList(), AppendUnderscoreFormat, rewriteSession);
                }
            }
        }

        private void RenameModule(RenameModel model, IRewriteSession rewriteSession)
        {
            RenameReferences(model.Target, model.NewName, rewriteSession);

            if (model.Target.DeclarationType.HasFlag(DeclarationType.ClassModule))
            {
                foreach (var reference in model.Target.References)
                {
                    var ctxt = reference.Context.GetAncestor<VBAParser.ImplementsStmtContext>();
                    if (ctxt != null)
                    {
                        RenameDefinedFormatMembers(model, _declarationFinderProvider.DeclarationFinder.FindInterfaceMembersForImplementsContext(ctxt).ToList(), AppendUnderscoreFormat, rewriteSession);
                    }
                }
            }

            var component = _projectsProvider.Component(model.Target.QualifiedName.QualifiedModuleName);
            switch (component.Type)
            {
                case ComponentType.Document:
                    {
                        using (var properties = component.Properties)
                        using (var property = properties["_CodeName"])
                        {
                            property.Value = model.NewName;
                        }
                        break;
                    }
                case ComponentType.UserForm:
                case ComponentType.VBForm:
                case ComponentType.MDIForm:
                    {
                        using (var properties = component.Properties)
                        using (var property = properties["Caption"])
                        {
                            if ((string)property.Value == model.Target.IdentifierName)
                            {
                                property.Value = model.NewName;
                            }
                            component.Name = model.NewName;
                        }
                        break;
                    }
                default:
                    {
                        using (var vbe = component.VBE)
                        {
                            if (vbe.Kind == VBEKind.Hosted)
                            {
                                // VBA - rename code module
                                using (var codeModule = component.CodeModule)
                                {
                                    Debug.Assert(!codeModule.IsWrappingNullReference,
                                        "input validation fail: Attempting to rename an ICodeModule wrapping a null reference");
                                    codeModule.Name = model.NewName;
                                }
                            }
                            else
                            {
                                // VB6 - rename component
                                component.Name = model.NewName;
                            }
                        }
                        break;
                    }
            }
        }

        //TODO: Implement renaming references to the project in code.
        private void RenameProject(RenameModel model, IRewriteSession rewriteSession)
        {
            var project = _projectsProvider.Project(model.Target.ProjectId);

            if (project != null)
            {
                project.Name = model.NewName;
            }
        }

        private void RenameDefinedFormatMembers(RenameModel model, IReadOnlyCollection<Declaration> members, string underscoreFormat, IRewriteSession rewriteSession)
        {
            if (!members.Any()) { return; }

            var targetFragment = string.Format(underscoreFormat, model.Target.IdentifierName);
            var replacementFragment = string.Format(underscoreFormat, model.NewName);
            foreach (var member in members)
            {
                var newMemberName = member.IdentifierName.Replace(targetFragment, replacementFragment);
                RenameStandardElements(member, newMemberName, rewriteSession);
            }
        }

        private void RenameStandardElements(Declaration target, string newName, IRewriteSession rewriteSession)
        {
            RenameReferences(target, newName, rewriteSession);
            RenameDeclaration(target, newName, rewriteSession);
        }

        private void RenameReferences(Declaration target, string newName, IRewriteSession rewriteSession)
        {
            var modules = target.References
                .Where(reference =>
                    reference.Context.GetText() != "Me"
                    && !reference.IsArrayAccess
                    && !reference.IsDefaultMemberAccess)
                .GroupBy(r => r.QualifiedModuleName);

            foreach (var grouping in modules)
            {
                var rewriter = rewriteSession.CheckOutModuleRewriter(grouping.Key);
                foreach (var reference in grouping)
                {
                    rewriter.Replace(reference.Context, newName);
                }
            }
        }

        private void RenameDeclaration(Declaration target, string newName, IRewriteSession rewriteSession)
        {
            var rewriter = rewriteSession.CheckOutModuleRewriter(target.QualifiedName.QualifiedModuleName);

            if (target.Context is IIdentifierContext context)
            {
                rewriter.Replace(context.IdentifierTokens, newName);
            }
        }

        private IEnumerable<Declaration> FindEventHandlersForControl(Declaration control)
        {
            if (control != null && control.DeclarationType.HasFlag(DeclarationType.Control))
            {
                return _declarationFinderProvider.DeclarationFinder.FindEventHandlers()
                    .Where(ev => ev.Scope.StartsWith($"{control.ParentScope}.{control.IdentifierName}_"));
            }

            return Enumerable.Empty<Declaration>();
        }
    }
}