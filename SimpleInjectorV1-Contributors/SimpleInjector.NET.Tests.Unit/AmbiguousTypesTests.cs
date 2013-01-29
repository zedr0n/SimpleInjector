﻿namespace SimpleInjector.Tests.Unit
{
    using System;
    using System.Globalization;
    using System.Reflection;

    using NUnit.Framework;

    [TestFixture]
    public class AmbiguousTypesTests
    {
        [Test]
        public void RegisterFunc_SuppliedWithAmbiguousTypeString_ThrowsExpectedException()
        {
            // Arrange
            var container = new Container();

            // Act
            Assert_RegistrationFailsWithExpectedAmbiguousMessage("String", () =>
            {
                container.Register<string>(() => "some value");
            });
        }

        [Test]
        public void RegisterFunc_SuppliedWithAmbiguousTypeType_ThrowsExpectedException()
        {
            // Arrange
            var container = new Container();

            // Act
            Assert_RegistrationFailsWithExpectedAmbiguousMessage("Type", () =>
            {
                container.Register<Type>(() => typeof(int));
            });
        }

        [Test]
        public void RegisterSingleFunc_SuppliedWithAmbiguousType_ThrowsExpectedException()
        {
            // Arrange
            var container = new Container();

            // Act
            Assert_RegistrationFailsWithExpectedAmbiguousMessage("String", () =>
            {
                container.RegisterSingle<string>(() => "some value");
            });
        }

        [Test]
        public void RegisterSingleValue_SuppliedWithAmbiguousType_ThrowsExpectedException()
        {
            // Arrange
            var container = new Container();

            // Act
            Assert_RegistrationFailsWithExpectedAmbiguousMessage("String", () =>
            {
                container.RegisterSingle<string>("some value");
            });
        }

        [Test]
        public void RegisterFunc_SuppliedWithAmbiguousType_ThrowsExceptionWithExpectedParamName()
        {
            // Arrange
            var container = new Container();
            
            // Assert
            Assert_RegistrationFailsWithExpectedParamName("TService", () =>
            {
                // Act
                container.Register<string>(() => "some value");
            });
        }

        [Test]
        public void RegisterSingleFunc_SuppliedWithAmbiguousType_ThrowsExceptionWithExpectedParamName()
        {
            // Arrange
            var container = new Container();

            // Assert
            Assert_RegistrationFailsWithExpectedParamName("TService", () =>
            {
                // Act
                container.RegisterSingle<string>(() => "some value");
            });
        }

        [Test]
        public void RegisterSingleValue_SuppliedWithAmbiguousType_ThrowsExceptionWithExpectedParamName()
        {
            // Arrange
            var container = new Container();

            // Assert
            Assert_RegistrationFailsWithExpectedParamName("TService", () =>
            {
                // Act
                container.RegisterSingle<string>("some value");
            });
        }

        private static void Assert_RegistrationFailsWithExpectedParamName(string paramName, Action action)
        {
            try
            {
                // Act
                action();

                // Assert
                Assert.Fail("Exception expected.");
            }
            catch (ArgumentException ex)
            {
                AssertThat.ExceptionContainsParamName(ex, "TService");
            }
        }

        private static void Assert_RegistrationFailsWithExpectedAmbiguousMessage(string typeName, Action action)
        {
            try
            {
                // Act
                action();

                // Assert
                Assert.Fail("Exception expected.");
            }
            catch (ArgumentException ex)
            {
                string message = @"
                    You are trying to register " + typeName + @" as a service type, but registering this type
                    is not allowed to be registered because the type is ambiguous";

                AssertThat.ExceptionMessageContains(message.TrimInside(), ex);
            }
        }
    }
}