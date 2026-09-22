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
}
