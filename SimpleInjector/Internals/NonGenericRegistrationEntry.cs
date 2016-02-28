﻿#region Copyright Simple Injector Contributors
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (c) 2015 Simple Injector Contributors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
 * associated documentation files (the "Software"), to deal in the Software without restriction, including 
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the 
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial 
 * portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO 
 * EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion

namespace SimpleInjector.Internals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal class TargetTypeInfo : IEquatable<TargetTypeInfo>
    {
        public Type ServiceType { get; }
        public Type ImplementationType { get; }
        public int? HashCode { get; }

        public static List<Type> SimpleType = new List<Type>(); 

        public TargetTypeInfo(PredicateContext context)
            : this(context.ServiceType, context.ImplementationType, context.Consumer)
        {
        }

        public TargetTypeInfo(Type serviceType, Type implementationType = null,InjectionConsumerInfo consumerInfo = null)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            HashCode = consumerInfo?.Target?.Member?.GetCustomAttributes(true)?.Sum(attr => attr.GetHashCode());
            if (SimpleType.Any(x => x.IsAssignableFrom(ImplementationType)))
                return;

            for (var consumer = consumerInfo?.ParentInfo; consumer != null; consumer = consumer.ParentInfo)
            {
                HashCode += consumer.Target?.Member?.GetCustomAttributes(true)?.Sum(attr => attr.GetHashCode());
                HashCode += consumer.ImplementationType.GetCustomAttributes(true).Sum(attr => attr.GetHashCode());
            }
        }

        public bool Equals(TargetTypeInfo other)
        {
            return ImplementationType == other.ImplementationType && HashCode == other.HashCode && ServiceType == other.ServiceType;
            //ConsumerType == other.ConsumerType;
        }

        public override int GetHashCode()
        {
            var hashCode = 0;

            hashCode += ImplementationType?.Name.GetHashCode() ?? 0;
            hashCode += ServiceType?.Name.GetHashCode() ?? 0;
            hashCode += HashCode ?? 0;

            return hashCode;
        }
    }
    internal sealed class NonGenericRegistrationEntry : IRegistrationEntry
    {
        private readonly List<IProducerProvider> providers = new List<IProducerProvider>(1);
        private readonly Type nonGenericServiceType;
        private readonly Container container;

        public NonGenericRegistrationEntry(Type nonGenericServiceType, Container container)
        {
            this.nonGenericServiceType = nonGenericServiceType;
            this.container = container;
        }

        private interface IProducerProvider
        {
            IEnumerable<InstanceProducer> CurrentProducers { get; }

            InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled);
        }

        public IEnumerable<InstanceProducer> CurrentProducers => this.providers.SelectMany(p => p.CurrentProducers);
        private IEnumerable<InstanceProducer> ConditionalProducers => this.CurrentProducers.Where(p => p.IsConditional);
        private IEnumerable<InstanceProducer> UnconditionalProducers => this.CurrentProducers.Where(p => !p.IsConditional);
        public int GetNumberOfConditionalRegistrationsFor(Type serviceType) => this.CurrentProducers.Count(p => p.IsConditional);

        public void Add(InstanceProducer producer)
        {
            this.container.ThrowWhenContainerIsLocked();
            this.ThrowWhenConditionalAndUnconditionalAreMixed(producer);
            this.ThrowWhenConditionalIsRegisteredInOverridingMode(producer);

            this.ThrowWhenTypeAlreadyRegistered(producer);
            this.ThrowWhenIdenticalImplementationIsAlreadyRegistered(producer);

            if (producer.IsUnconditional)
            {
                this.providers.Clear();
            }

            this.providers.Add(new SingleInstanceProducerProvider(producer));
        }

        public void Add(Type serviceType, Func<TypeFactoryContext, Type> implementationTypeFactory,
            Lifestyle lifestyle, Predicate<PredicateContext> predicate)
        {
            Requires.IsNotNull(predicate, "only support conditional for now");

            this.container.ThrowWhenContainerIsLocked();

            if (this.UnconditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(serviceType));
            }

            this.providers.Add(new ImplementationTypeFactoryInstanceProducerProvider(serviceType,
                implementationTypeFactory, lifestyle, predicate, this.container));
        }

        public InstanceProducer TryGetInstanceProducer(Type serviceType, InjectionConsumerInfo context)
        {
            var instanceProducers = this.GetInstanceProducers(context).ToArray();

            if (instanceProducers.Length <= 1)
            {
                return instanceProducers.FirstOrDefault();
            }

            throw this.ThrowMultipleApplicableRegistrationsFound(instanceProducers);
        }

        public void AddGeneric(Type serviceType, Type implementationType,
            Lifestyle lifestyle, Predicate<PredicateContext> predicate)
        {
            throw new NotSupportedException();
        }

        private IEnumerable<InstanceProducer> GetInstanceProducers(InjectionConsumerInfo consumer)
        {
            bool handled = false;

            foreach (var provider in this.providers)
            {
                InstanceProducer producer = provider.TryGetProducer(consumer, handled);

                if (producer != null)
                {
                    yield return producer;
                    handled = true;
                }
            }
        }

        private void ThrowWhenTypeAlreadyRegistered(InstanceProducer producer)
        {
            if (producer.IsUnconditional && this.providers.Any() &&
                !this.container.Options.AllowOverridingRegistrations)
            {
                throw new InvalidOperationException(StringResources.TypeAlreadyRegistered(this.nonGenericServiceType));
            }
        }

        private void ThrowWhenIdenticalImplementationIsAlreadyRegistered(
            InstanceProducer producerToRegister)
        {
            // A provider overlaps the providerToRegister when it can be applied to ALL generic
            // types that the providerToRegister can be applied to as well.
            var overlappingProducers = this.GetOverlappingProducers(producerToRegister);

            bool isReplacement =
                producerToRegister.IsUnconditional && this.container.Options.AllowOverridingRegistrations;

            if (!isReplacement && overlappingProducers.Any())
            {
                var overlappingProducer = overlappingProducers.FirstOrDefault();

                throw new InvalidOperationException(
                    StringResources.AnOverlappingRegistrationExists(
                        producerToRegister.ServiceType,
                        overlappingProducer.ImplementationType,
                        overlappingProducer.IsConditional,
                        producerToRegister.ImplementationType,
                        producerToRegister.IsConditional));
            }
        }

        private IEnumerable<InstanceProducer> GetOverlappingProducers(InstanceProducer producerToRegister)
        {
            return
                from producer in this.CurrentProducers
                where producer.ImplementationType != null
                where !producer.Registration.WrapsInstanceCreationDelegate
                where !producerToRegister.Registration.WrapsInstanceCreationDelegate
                where producer.ImplementationType == producerToRegister.ImplementationType
                select producer;
        }

        private ActivationException ThrowMultipleApplicableRegistrationsFound(
            InstanceProducer[] instanceProducers)
        {
            var producersInfo =
                from producer in instanceProducers
                select Tuple.Create(this.nonGenericServiceType, producer.Registration.ImplementationType, producer);

            return new ActivationException(
                StringResources.MultipleApplicableRegistrationsFound(
                    this.nonGenericServiceType, producersInfo.ToArray()));
        }

        private void ThrowWhenConditionalAndUnconditionalAreMixed(InstanceProducer producer)
        {
            this.ThrowWhenNonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(producer);
            this.ThrowWhenNonGenericTypeAlreadyRegisteredAsConditionalRegistration(producer);
        }

        private void ThrowWhenConditionalIsRegisteredInOverridingMode(InstanceProducer producer)
        {
            if (producer.IsConditional && this.container.Options.AllowOverridingRegistrations)
            {
                throw new NotSupportedException(
                    StringResources.MakingConditionalRegistrationsInOverridingModeIsNotSupported());
            }
        }

        private void ThrowWhenNonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(
            InstanceProducer producer)
        {
            if (producer.IsConditional && this.UnconditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(
                        producer.ServiceType));
            }
        }

        private void ThrowWhenNonGenericTypeAlreadyRegisteredAsConditionalRegistration(
            InstanceProducer producer)
        {
            if (producer.IsUnconditional && this.ConditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsConditionalRegistration(
                        producer.ServiceType));
            }
        }

        private sealed class SingleInstanceProducerProvider : IProducerProvider
        {
            private readonly Dictionary<TargetTypeInfo, InstanceProducer> cache = new Dictionary<TargetTypeInfo, InstanceProducer>();
            private readonly InstanceProducer _producer;

            public SingleInstanceProducerProvider(InstanceProducer producer)
            {
                _producer = producer;
            }

            public IEnumerable<InstanceProducer> CurrentProducers => Enumerable.Repeat(this._producer, 1);

            public InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled)
            {
                var context = new PredicateContext(_producer, consumer, handled);

                InstanceProducer producer = null;

                lock (this.cache)
                {
                    var targetInfo = new TargetTypeInfo(context);

                    if (!this.cache.TryGetValue(targetInfo, out producer))
                    {
                        this.cache[targetInfo] = producer = this.CreateNewProducerFor(context);
                    }
                }

                return producer.Predicate(new PredicateContext(producer, consumer, handled))
                    ? producer
                    : null;
            }

            private InstanceProducer CreateNewProducerFor(PredicateContext context) =>
                new InstanceProducer(
                    _producer.ServiceType,
                    _producer.Registration,
                    _producer.Predicate,
                    context.Consumer);
        }
        private class ImplementationTypeFactoryInstanceProducerProvider : IProducerProvider
        {
            private readonly Dictionary<TargetTypeInfo, InstanceProducer> cache = new Dictionary<TargetTypeInfo, InstanceProducer>();
            //private readonly Dictionary<Tuple<Type,int?>, InstanceProducer> cache = new Dictionary<Tuple<Type,int?>, InstanceProducer>();
            private readonly Func<TypeFactoryContext, Type> implementationTypeFactory;
            private readonly Lifestyle lifestyle;
            private readonly Predicate<PredicateContext> predicate;
            private readonly Type serviceType;
            private readonly Container container;

            public ImplementationTypeFactoryInstanceProducerProvider(Type serviceType,
                Func<TypeFactoryContext, Type> implementationTypeFactory, Lifestyle lifestyle,
                Predicate<PredicateContext> predicate, Container container)
            {
                this.serviceType = serviceType;
                this.implementationTypeFactory = implementationTypeFactory;
                this.lifestyle = lifestyle;
                this.predicate = predicate;
                this.container = container;
            }

            public IEnumerable<InstanceProducer> CurrentProducers
            {
                get
                {
                    lock (this.cache)
                    {
                        return this.cache.Values.ToArray();
                    }
                }
            }

            public InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled)
            {
                Func<Type> implementationTypeProvider = 
                    () => this.GetImplementationTypeThroughFactory(consumer);

                var context = 
                    new PredicateContext(this.serviceType, implementationTypeProvider, consumer, handled);

                // NOTE: The producer should only get built after it matches the delegate, to prevent
                // unneeded producers from being created, because this might cause diagnostic warnings, 
                // such as torn lifestyle warnings.
                return this.predicate(context) ? this.GetProducer(context) : null;
            }

            private Type GetImplementationTypeThroughFactory(InjectionConsumerInfo consumer)
            {
                var context = new TypeFactoryContext(this.serviceType, consumer);

                Type implementationType = this.implementationTypeFactory(context);

                if (implementationType == null)
                {
                    throw new InvalidOperationException(StringResources.FactoryReturnedNull(this.serviceType));
                }

                if (implementationType.Info().ContainsGenericParameters)
                {
                    throw new ActivationException(
                        StringResources.TheTypeReturnedFromTheFactoryShouldNotBeOpenGeneric(
                            this.serviceType, implementationType));
                }

                Requires.FactoryReturnsATypeThatIsAssignableFromServiceType(this.serviceType, implementationType);

                return implementationType;
            }

            private InstanceProducer GetProducer(PredicateContext context)
            {
                InstanceProducer producer;

                // Never build a producer twice. This could cause components with a torn lifestyle.
                lock (this.cache)
                {
                    var targetInfo = new TargetTypeInfo(context);
                    
                    // We need to cache on implementation, because service type is always the same.
                    if (!this.cache.TryGetValue(targetInfo, out producer))
                    {
                        this.cache[targetInfo] = producer = this.CreateNewProducerFor(context);
                    }
                }

                return producer;
            }

            private InstanceProducer CreateNewProducerFor(PredicateContext context) =>
                new InstanceProducer(
                    this.serviceType,
                    this.lifestyle.CreateRegistration(context.ServiceType, context.ImplementationType, 
                        this.container),
                    this.predicate,
                    context.Consumer);
        }
    }
}