﻿using System.Linq;
using Rubberduck.Parsing.Rewriter;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.UIContext;
using Rubberduck.Parsing.VBA;
using Rubberduck.Refactorings.Exceptions;
using Rubberduck.Refactorings.Exceptions.RemoveParameter;
using Rubberduck.VBEditor;
using Rubberduck.VBEditor.Utility;

namespace Rubberduck.Refactorings.RemoveParameters
{
    public class RemoveParametersRefactoring : InteractiveRefactoringBase<IRemoveParametersPresenter, RemoveParametersModel>
    {
        private readonly IBaseRefactoring<RemoveParametersModel> _baseRefactoring;
        private readonly IDeclarationFinderProvider _declarationFinderProvider;
        private readonly ISelectedDeclarationProvider _selectedDeclarationProvider;

        public RemoveParametersRefactoring(
            RemoveParameterBaseRefactoring baseRefactoring,
            IDeclarationFinderProvider declarationFinderProvider, 
            IRefactoringPresenterFactory factory, 
            IRewritingManager rewritingManager,
            ISelectionProvider selectionProvider,
            ISelectedDeclarationProvider selectedDeclarationProvider,
            IUiDispatcher uiDispatcher)
        :base(rewritingManager, selectionProvider, factory, uiDispatcher)
        {
            _baseRefactoring = baseRefactoring;
            _declarationFinderProvider = declarationFinderProvider;
            _selectedDeclarationProvider = selectedDeclarationProvider;
        }

        protected override Declaration FindTargetDeclaration(QualifiedSelection targetSelection)
        {
            var selectedDeclaration = _selectedDeclarationProvider.SelectedDeclaration(targetSelection);
            if (!ValidDeclarationTypes.Contains(selectedDeclaration.DeclarationType))
            {
                return selectedDeclaration.DeclarationType == DeclarationType.Parameter
                    ? _selectedDeclarationProvider.SelectedMember(targetSelection)
                    : null;
            }

            return selectedDeclaration;
        }

        protected override RemoveParametersModel InitializeModel(Declaration target)
        {
            if (target == null)
            {
                throw new TargetDeclarationIsNullException();
            }

            if (!ValidDeclarationTypes.Contains(target.DeclarationType) && target.DeclarationType != DeclarationType.Parameter)
            {
                throw new InvalidDeclarationTypeException(target);
            }

            var model = DerivedTarget(new RemoveParametersModel(target));

            return model;
        }

        private RemoveParametersModel DerivedTarget(RemoveParametersModel model)
        {
            var preliminaryModel = ResolvedInterfaceMemberTarget(model) 
                                   ?? ResolvedEventTarget(model) 
                                   ?? model;
            return ResolvedGetterTarget(preliminaryModel) ?? preliminaryModel;
        }

        private static RemoveParametersModel ResolvedInterfaceMemberTarget(RemoveParametersModel model)
        {
            var declaration = model.TargetDeclaration;
            if (!(declaration is ModuleBodyElementDeclaration member) || !member.IsInterfaceImplementation)
            {
                return null;
            }

            model.IsInterfaceMemberRefactoring = true;
            model.TargetDeclaration = member.InterfaceMemberImplemented;

            return model;
        }

        private RemoveParametersModel ResolvedEventTarget(RemoveParametersModel model)
        {
            foreach (var eventDeclaration in _declarationFinderProvider
                .DeclarationFinder
                .UserDeclarations(DeclarationType.Event))
            {
                if (_declarationFinderProvider.DeclarationFinder
                    .FindEventHandlers(eventDeclaration)
                    .Any(handler => Equals(handler, model.TargetDeclaration)))
                {
                    model.IsEventRefactoring = true;
                    model.TargetDeclaration = eventDeclaration;
                    return model;
                }
            }
            return null;
        }

        private RemoveParametersModel ResolvedGetterTarget(RemoveParametersModel model)
        {
            var target = model.TargetDeclaration;
            if (target == null || !target.DeclarationType.HasFlag(DeclarationType.Property))
            {
                return null;
            }

            if (target.DeclarationType == DeclarationType.PropertyGet)
            {
                model.IsPropertyRefactoringWithGetter = true;
                return model;
            }


            var getter = _declarationFinderProvider.DeclarationFinder
                .UserDeclarations(DeclarationType.PropertyGet)
                .FirstOrDefault(item => item.Scope == target.Scope 
                                        && item.IdentifierName == target.IdentifierName);

            if (getter == null)
            {
                return null;
            }

            model.IsPropertyRefactoringWithGetter = true;
            model.TargetDeclaration = getter;

            return model;
        }

        protected override void RefactorImpl(RemoveParametersModel model)
        {
            if (model.TargetDeclaration == null)
            {
                throw new TargetDeclarationIsNullException();
            }

            _baseRefactoring.Refactor(model);
        }

        public void QuickFix(QualifiedSelection selection)
        {
            var targetDeclaration = FindTargetDeclaration(selection);
            var model = InitializeModel(targetDeclaration);
            
            var selectedParameters = model.Parameters.Where(p => selection.Selection.Contains(p.Declaration.QualifiedSelection.Selection)).ToList();

            if (selectedParameters.Count > 1)
            {
                throw new MultipleParametersSelectedException(selectedParameters);
            }

            var target = selectedParameters.SingleOrDefault(p => selection.Selection.Contains(p.Declaration.QualifiedSelection.Selection));

            if (target == null)
            {
                throw new NoParameterSelectedException();
            }

            model.RemoveParameters.Add(target);
            _baseRefactoring.Refactor(model);
        }

        public static readonly DeclarationType[] ValidDeclarationTypes =
        {
            DeclarationType.Event,
            DeclarationType.Function,
            DeclarationType.Procedure,
            DeclarationType.PropertyGet,
            DeclarationType.PropertyLet,
            DeclarationType.PropertySet
        };
    }
}
