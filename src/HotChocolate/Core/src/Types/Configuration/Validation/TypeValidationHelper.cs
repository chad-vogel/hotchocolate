using System.Collections.Generic;
using System.Linq;
using HotChocolate.Types;
using static HotChocolate.Configuration.Validation.ErrorHelper;

#nullable enable

namespace HotChocolate.Configuration.Validation;

internal static class TypeValidationHelper
{
    private const string _twoUnderscores = "__";

    public static void EnsureTypeHasFields(
        IComplexOutputType type,
        ICollection<ISchemaError> errors)
    {
        if (type.Fields.Count == 0 ||
            type.Fields.All(t => t.IsIntrospectionField))
        {
            errors.Add(NeedsOneAtLeastField(type));
        }
    }

    public static void EnsureFieldDeprecationIsValid(
        IInputObjectType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Fields.Count; i++)
        {
            IInputField field = type.Fields[i];

            if (field.IsDeprecated && field.Type.IsNonNullType())
            {
                errors.Add(RequiredFieldCannotBeDeprecated(type, field));
            }
        }
    }

    public static void EnsureArgumentDeprecationIsValid(
        IComplexOutputType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Fields.Count; i++)
        {
            IOutputField field = type.Fields[i];
            for (var j = 0; j < field.Arguments.Count; j++)
            {
                IInputField argument = field.Arguments[j];

                if (argument.IsDeprecated && argument.Type.IsNonNullType())
                {
                    errors.Add(RequiredArgumentCannotBeDeprecated(type, field, argument));
                }
            }
        }
    }

    public static void EnsureArgumentDeprecationIsValid(
        DirectiveType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            Argument argument = type.Arguments[i];
            if (argument.IsDeprecated && argument.Type.IsNonNullType())
            {
                errors.Add(RequiredArgumentCannotBeDeprecated(type, argument));
            }
        }
    }

    public static void EnsureTypeHasFields(
        InputObjectType type,
        ICollection<ISchemaError> errors)
    {
        if (type.Fields.Count == 0)
        {
            errors.Add(NeedsOneAtLeastField(type));
        }
    }

    public static void EnsureFieldNamesAreValid(
        IComplexOutputType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Fields.Count; i++)
        {
            IOutputField field = type.Fields[i];

            if (!field.IsIntrospectionField)
            {
                if (field.Name.Value.StartsWith(_twoUnderscores))
                {
                    errors.Add(TwoUnderscoresNotAllowedField(type, field));
                }

                for (var j = 0; j < field.Arguments.Count; j++)
                {
                    IInputField argument = field.Arguments[j];

                    if (argument.Name.Value.StartsWith(_twoUnderscores))
                    {
                        errors.Add(TwoUnderscoresNotAllowedOnArgument(
                            type,
                            field,
                            argument));
                    }
                }
            }
        }
    }

    public static void EnsureFieldNamesAreValid(
        InputObjectType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Fields.Count; i++)
        {
            InputField field = type.Fields[i];

            if (field.Name.Value.StartsWith(_twoUnderscores))
            {
                errors.Add(TwoUnderscoresNotAllowedField(type, field));
            }
        }
    }

    public static void EnsureArgumentNamesAreValid(
        DirectiveType type,
        ICollection<ISchemaError> errors)
    {
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            IInputField field = type.Arguments[i];

            if (field.Name.Value.StartsWith(_twoUnderscores))
            {
                errors.Add(TwoUnderscoresNotAllowedOnArgument(type, field));
            }
        }
    }

    public static void EnsureInterfacesAreCorrectlyImplemented(
        IComplexOutputType type,
        ICollection<ISchemaError> errors)
    {
        if (type.Implements.Count > 0)
        {
            foreach (IInterfaceType implementedType in type.Implements)
            {
                ValidateImplementation(type, implementedType, errors);
            }
        }
    }

    // http://spec.graphql.org/draft/#IsValidImplementation()
    private static void ValidateImplementation(
        IComplexOutputType type,
        IInterfaceType implementedType,
        ICollection<ISchemaError> errors)
    {
        if (!IsFullyImplementingInterface(type, implementedType))
        {
            errors.Add(NotTransitivelyImplemented(type, implementedType));
        }

        foreach (IOutputField implementedField in implementedType.Fields)
        {
            if (type.Fields.TryGetField(implementedField.Name, out IOutputField? field))
            {
                ValidateArguments(field, implementedField, errors);

                if (!IsValidImplementationFieldType(field.Type, implementedField.Type))
                {
                    errors.Add(InvalidFieldType(type, field, implementedField));
                }
            }
            else
            {
                errors.Add(FieldNotImplemented(type, implementedField));
            }
        }
    }

    private static void ValidateArguments(
        IOutputField field,
        IOutputField implementedField,
        ICollection<ISchemaError> errors)
    {
        var implArgs = implementedField.Arguments.ToDictionary(t => t.Name);

        foreach (IInputField? argument in field.Arguments)
        {
            if (implArgs.TryGetValue(argument.Name, out IInputField? implementedArgument))
            {
                implArgs.Remove(argument.Name);
                if (!argument.Type.IsEqualTo(implementedArgument.Type))
                {
                    errors.Add(InvalidArgumentType(
                        field,
                        implementedField,
                        argument,
                        implementedArgument));
                }
            }
            else if (argument.Type.IsNonNullType())
            {
                errors.Add(AdditionalArgumentNotNullable(
                    field,
                    implementedField,
                    argument));
            }
        }

        foreach (IInputField? missingArgument in implArgs.Values)
        {
            errors.Add(ArgumentNotImplemented(
                field,
                implementedField,
                missingArgument));
        }
    }

    private static bool IsFullyImplementingInterface(
        IComplexOutputType type,
        IInterfaceType implementedType)
    {
        foreach (IInterfaceType? interfaceType in implementedType.Implements)
        {
            if (!type.IsImplementing(interfaceType))
            {
                return false;
            }
        }

        return true;
    }

    // http://spec.graphql.org/draft/#IsValidImplementationFieldType()
    private static bool IsValidImplementationFieldType(
        IOutputType fieldType,
        IOutputType implementedType)
    {
        if (fieldType.IsNonNullType())
        {
            fieldType = (IOutputType)fieldType.InnerType();

            if (implementedType.IsNonNullType())
            {
                implementedType = (IOutputType)implementedType.InnerType();
            }

            return IsValidImplementationFieldType(fieldType, implementedType);
        }

        if (fieldType.IsListType() && implementedType.IsListType())
        {
            return IsValidImplementationFieldType(
                (IOutputType)fieldType.ElementType(),
                (IOutputType)implementedType.ElementType());
        }

        if (ReferenceEquals(fieldType, implementedType))
        {
            return true;
        }

        if (fieldType is ObjectType objectType &&
            implementedType is UnionType unionType &&
            unionType.IsAssignableFrom(objectType))
        {
            return true;
        }

        if (fieldType is IComplexOutputType complexType &&
            implementedType is InterfaceType interfaceType &&
            complexType.IsImplementing(interfaceType))
        {
            return true;
        }

        return false;
    }
}
