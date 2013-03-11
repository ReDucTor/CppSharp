﻿using System.Collections.Generic;
using Cxxi.Types;

namespace Cxxi
{
    /// <summary>
    /// This type checker is used to check if a type is complete.
    /// </summary>
    public class TypeCompletionChecker : AstVisitor
    {
        public override bool VisitDeclaration(Declaration decl)
        {
            if (decl.CompleteDeclaration != null)
                return true;

            return !decl.IsIncomplete;
        }

        public override bool VisitBuiltinType(BuiltinType builtin, TypeQualifiers quals)
        {
            return true;
        }

        public override bool VisitFunctionType(FunctionType function, TypeQualifiers quals)
        {
            if (!function.ReturnType.Visit(this))
                return false;

            foreach (var arg in function.Parameters)
            {
                if (!arg.Type.Visit(this))
                    return false;
            }

            return true;
        }

        public override bool VisitFunctionDecl(Function function)
        {
            if (!function.ReturnType.Visit(this))
                return false;

            foreach (var param in function.Parameters)
            {
                if (!param.Visit(this))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// This type checker is used to check if a type is ignored.
    /// </summary>
    public class TypeIgnoreChecker : AstVisitor
    {
        ITypeMapDatabase TypeMapDatabase { get; set; }
        public bool IsIgnored;

        public TypeIgnoreChecker(ITypeMapDatabase database)
        {
            TypeMapDatabase = database;
            IsIgnored = false;
        }

        void Ignore()
        {
            IsIgnored = true;
        }

        public override bool VisitDeclaration(Declaration decl)
        {
            if (decl.Ignore)
                Ignore();

            return true;
        }

        public override bool VisitClassDecl(Class @class)
        {
            if (AlreadyVisited(@class))
                return false;

            VisitDeclaration(@class);
            return true;
        }

        public override bool VisitTypedefType(TypedefType typedef,
            TypeQualifiers quals)
        {
            TypeMap typeMap = null;
            if (TypeMapDatabase.FindTypeMap(typedef, out typeMap)
                && typeMap.IsIgnored)
            {
                Ignore();
                return false;
            }

            return base.VisitTypedefType(typedef, quals);
        }

        public override bool VisitTypedefDecl(TypedefDecl typedef)
        {
            TypeMap typeMap = null;
            if (TypeMapDatabase.FindTypeMap(typedef, out typeMap)
                && typeMap.IsIgnored)
            {
                Ignore();
                return false;
            }

            return base.VisitTypedefDecl(typedef);
        }

        public override bool VisitTemplateSpecializationType(
            TemplateSpecializationType template, TypeQualifiers quals)
        {
            var decl = template.Template.TemplatedDecl;

            TypeMap typeMap = null;
            if (TypeMapDatabase.FindTypeMap(decl, out typeMap)
                && typeMap.IsIgnored)
            {
                Ignore();
                return false;
            }

            return base.VisitTemplateSpecializationType(template, quals);
        }
    }

    /// <summary>
    /// This is used to get the declarations that each file needs to forward
    /// reference or include from other header files. Since in C++ everything
    /// that is referenced needs to have been declared previously, it can happen
    /// that a file needs to be reference something that has not been declared
    /// yet. In that case, we need to declare it before referencing it.
    /// </summary>
    public class TypeRefsVisitor : AstVisitor
    {
        public ISet<Declaration> ForwardReferences;
        public ISet<Class> Bases;
        private TranslationUnit unit;

        public TypeRefsVisitor()
        {
            ForwardReferences = new HashSet<Declaration>();
            Bases = new HashSet<Class>();
        }

        public void Collect(Declaration declaration)
        {
            var @namespace = declaration.Namespace;

            if (@namespace != null)
                if (@namespace.TranslationUnit.IsSystemHeader)
                    return;

            ForwardReferences.Add(declaration);
        }

        public bool VisitTranslationUnit(TranslationUnit unit)
        {
            this.unit = unit;
            unit.TypeReferences = this;

            VisitNamespace(unit);

            foreach (var @namespace in unit.Namespaces)
                VisitNamespace(@namespace);

            return true;
        }

        public override bool VisitClassDecl(Class @class)
        {
            if (@class.Ignore)
            {
                Visited.Add(@class);
                return false;
            }

            if (Visited.Contains(@class))
                return ForwardReferences.Contains(@class);

            Collect(@class);

            // If the class is incomplete, then we cannot process the record
            // members, else it will add references to declarations that
            // should have not been found.
            if (@class.IsIncomplete)
                goto OutVisited;

            var unitClass = unit.FindClass(@class.Name);
            if (unitClass == null || unitClass.IsIncomplete)
                goto OutVisited;

            foreach (var @base in @class.Bases)
            {
                if (!@base.IsClass)
                    continue;

                Bases.Add(@base.Class);
            }

            return base.VisitClassDecl(@class);

        OutVisited:

            Visited.Add(@class);
            return true;
        }

        public override bool VisitEnumDecl(Enumeration @enum)
        {
            Collect(@enum);
            return true;
        }

        public override bool VisitFieldDecl(Field field)
        {
            if (base.VisitFieldDecl(field))
                Collect(field);

            return true;
        }

        public override bool VisitTypedefType(TypedefType typedef,
            TypeQualifiers quals)
        {
            var decl = typedef.Declaration;

            if (decl.Type == null)
                return false;

            FunctionType function;
            if (decl.Type.IsPointerTo<FunctionType>(out function))
            {
                Collect(decl);
                return true;
            }

            return decl.Type.Visit(this);
        }
    }
}
