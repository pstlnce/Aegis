﻿namespace System.Diagnostics.CodeAnalysis;

//
// Summary:
//     Specifies that when a method returns System.Diagnostics.CodeAnalysis.NotNullWhenAttribute.ReturnValue,
//     the parameter will not be null even if the corresponding type allows it.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class NotNullWhenAttribute : Attribute
{
    //
    // Summary:
    //     Initializes the attribute with the specified return value condition.
    //
    // Parameters:
    //   returnValue:
    //     The return value condition. If the method returns this value, the associated
    //     parameter will not be null.
    public NotNullWhenAttribute(bool returnValue)
    {
        ReturnValue = returnValue;
    }

    //
    // Summary:
    //     Gets the return value condition.
    //
    // Returns:
    //     The return value condition. If the method returns this value, the associated
    //     parameter will not be null.
    public bool ReturnValue { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class MemberNotNullWhenAttribute : Attribute
{
    public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
    {
        ReturnValue = returnValue;
        Members = members;
    }

    public bool ReturnValue { get; }
    public string[] Members { get; }
}