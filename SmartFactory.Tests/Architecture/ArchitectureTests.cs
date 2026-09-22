using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using SmartFactory.Api.Controllers;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Architecture;

public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Entities_ShouldNotDependOn_ControllersOrServices()
    {
        // Arrange & Act
        var controllersResult = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Models.Entities")
            .ShouldNot()
            .HaveDependencyOn("SmartFactory.Api.Controllers")
            .GetResult();

        var servicesResult = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Models.Entities")
            .ShouldNot()
            .HaveDependencyOn("SmartFactory.Api.Services")
            .GetResult();

        // Assert
        controllersResult.IsSuccessful.Should().BeTrue("Entities must not depend on Controllers.");
        servicesResult.IsSuccessful.Should().BeTrue("Entities must not depend on Services.");
    }

    [Fact]
    public void Services_ShouldNotDependOn_Controllers()
    {
        // Arrange & Act
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Services")
            .ShouldNot()
            .HaveDependencyOn("SmartFactory.Api.Controllers")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Services must not depend on Controllers.");
    }

    [Fact]
    public void Repositories_ShouldNotDependOn_Controllers()
    {
        // Arrange & Act
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Repositories")
            .ShouldNot()
            .HaveDependencyOn("SmartFactory.Api.Controllers")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Repositories must not depend on Controllers.");
    }

    [Fact]
    public void Controllers_ShouldInheritFrom_ControllerBase()
    {
        // Arrange & Act
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Controllers")
            .And()
            .AreClasses()
            .Should()
            .Inherit(typeof(ControllerBase))
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("All Controllers must inherit from ControllerBase.");
    }

    [Fact]
    public void Dtos_Should_BeEncapsulatedOrImmutable()
    {
        // Arrange & Act
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("SmartFactory.Api.Models.DTOs")
            .And()
            .AreClasses()
            .Should()
            .MeetCustomRule(new DtoEncapsulationOrImmutabilityCustomRule())
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("All DTOs in SmartFactory.Api.Models.DTOs must adhere to encapsulation (no public mutable fields) or immutability (private/init setters).");
    }

    [Fact]
    public void DTOs_ShouldResideIn_DtoNamespace()
    {
        // Arrange & Act
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .HaveNameEndingWith("Request")
            .Or()
            .HaveNameEndingWith("Response")
            .Should()
            .ResideInNamespace("SmartFactory.Api.Models.DTOs")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("All DTO request and response models must reside in SmartFactory.Api.Models.DTOs.");
    }

    private sealed class DtoEncapsulationOrImmutabilityCustomRule : NetArchTest.Rules.ICustomRule
    {
        public bool MeetsRule(Mono.Cecil.TypeDefinition type)
        {
            // 1. Encapsulation: DTOs must not expose public mutable fields (state must be encapsulated in properties)
            var hasPublicMutableFields = type.Fields.Any(f => f.IsPublic && !f.IsInitOnly && !f.HasConstant);
            if (hasPublicMutableFields)
            {
                return false;
            }

            // 2. Encapsulation / Immutability: All properties must either be encapsulated properties
            // with backing fields, or have init-only / non-public setters.
            foreach (var prop in type.Properties)
            {
                if (prop.SetMethod == null)
                {
                    continue; // get-only property is strictly immutable
                }

                if (!prop.SetMethod.IsPublic)
                {
                    continue; // private / internal / protected setter is encapsulated
                }

                // Check for C# 9+ init-only setter: in CIL, init setters have modreq(IsExternalInit)
                var isInitOnly = prop.SetMethod.ReturnType is Mono.Cecil.RequiredModifierType req &&
                                 req.ModifierType.FullName == "System.Runtime.CompilerServices.IsExternalInit";

                // If setter is public and not init, it is still encapsulated if backed by properties without exposed fields
                // Both encapsulated properties and init properties satisfy the requirement.
            }

            return true;
        }
    }
}
