using System.Reflection;
using Shouldly;
using Xunit;

namespace LightestNight.System.EventSourcing.SqlStreamStore.Subscriptions.Tests
{
    public class ExtendsAssemblyTests
    {
        [Fact]
        public void Should_Output_Loadable_Types()
        {
            // Act
            var result = Assembly.GetExecutingAssembly().GetLoadableTypes();
            
            // Assert
            result.ShouldNotBeEmpty();
        }
    }
}