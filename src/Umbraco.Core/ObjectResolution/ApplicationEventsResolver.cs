using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Umbraco.Core.Logging;
using umbraco.interfaces;

namespace Umbraco.Core.ObjectResolution
{
    /// <summary>
	/// A resolver to return all IApplicationEvents objects
	/// </summary>
	/// <remarks>
	/// This is disposable because after the app has started it should be disposed to release any memory being occupied by instances.
	/// </remarks>
    public sealed class ApplicationEventsResolver : ManyObjectsResolverBase<ApplicationEventsResolver, IApplicationEventHandler>, IDisposable
	{
	    private readonly LegacyStartupHandlerResolver _legacyResolver;

	    /// <summary>
	    /// Constructor
	    /// </summary>
	    /// <param name="logger"></param>
	    /// <param name="applicationEventHandlers"></param>
	    /// <param name="serviceProvider"></param>
	    internal ApplicationEventsResolver(IServiceProvider serviceProvider, ILogger logger, IEnumerable<Type> applicationEventHandlers)
            : base(serviceProvider, logger, applicationEventHandlers)
		{
            //create the legacy resolver and only include the legacy types
	        _legacyResolver = new LegacyStartupHandlerResolver(
                serviceProvider, logger,
	            applicationEventHandlers.Where(x => TypeHelper.IsTypeAssignableFrom<IApplicationEventHandler>(x) == false));
		}

        /// <summary>
        /// Override in order to only return types of IApplicationEventHandler and above,
        /// do not include the legacy types of IApplicationStartupHandler
        /// </summary>
        protected override IEnumerable<Type> InstanceTypes
        {
            get { return base.InstanceTypes.Where(TypeHelper.IsTypeAssignableFrom<IApplicationEventHandler>); }
        }

	    private List<IApplicationEventHandler> _orderedAndFiltered;

        /// <summary>
        /// Gets the <see cref="IApplicationEventHandler"/> implementations.
        /// </summary>
        public IEnumerable<IApplicationEventHandler> ApplicationEventHandlers
		{
	        get
	        {
	            if (_orderedAndFiltered == null)
	            {
                    _orderedAndFiltered = GetSortedValues().ToList();                    
                    OnCollectionResolved(_orderedAndFiltered);
                }
	            return _orderedAndFiltered;
	        }
		}

        /// <summary>
        /// A delegate that can be set in the pre-boot phase in order to filter or re-order the event handler collection
        /// </summary>
        /// <remarks>
        /// This can be set on startup in the pre-boot process in either a custom boot manager or global.asax (UmbracoApplication)
        /// </remarks>
        public Action<IList<IApplicationEventHandler>> FilterCollection { get; set; }

        /// <summary>
        /// Allow any filters to be applied to the event handler list
        /// </summary>
        /// <param name="handlers"></param>
        /// <remarks>
        /// This allows custom logic to execute in order to filter or re-order the event handlers prior to executing,
        /// however this also ensures that any core handlers are executed first to ensure the stabiliy of Umbraco.
        /// </remarks>
        private void OnCollectionResolved(List<IApplicationEventHandler> handlers)
        {
            if (FilterCollection == null) return;

            FilterCollection(handlers);

            //find all of the core handlers and their weight, remove them from the main list
            var coreItems = new List<Tuple<IApplicationEventHandler, int>>();
            foreach (var handler in handlers.ToArray())
            {
                //Yuck, but not sure what else we can do 
                if (
                    handler.GetType().Assembly.FullName.StartsWith("Umbraco.", StringComparison.OrdinalIgnoreCase)
                    || handler.GetType().Assembly.FullName.StartsWith("Concorde."))
                {
                    coreItems.Add(new Tuple<IApplicationEventHandler, int>(handler, GetObjectWeight(handler)));
                    handlers.Remove(handler);
                }
            }

            //re-add the core handlers to the beginning of the list ordered by their weight
            foreach (var coreHandler in coreItems.OrderBy(x => x.Item2))
            {
                handlers.Insert(0, coreHandler.Item1);
            }            
        }

        /// <summary>
        /// Create instances of all of the legacy startup handlers
        /// </summary>
	    public void InstantiateLegacyStartupHandlers()
	    {
            //this will instantiate them all
	        var handlers = _legacyResolver.LegacyStartupHandlers;
	    }

		protected override bool SupportsClear
		{
            get { return false; }
		}

		protected override bool SupportsInsert
		{
			get { return false; }
		}

	    private class LegacyStartupHandlerResolver : ManyObjectsResolverBase<ApplicationEventsResolver, IApplicationStartupHandler>, IDisposable
	    {
	        internal LegacyStartupHandlerResolver(IServiceProvider serviceProvider, ILogger logger, IEnumerable<Type> legacyStartupHandlers)
                : base(serviceProvider, logger, legacyStartupHandlers)
	        {

	        }

            public IEnumerable<IApplicationStartupHandler> LegacyStartupHandlers
            {
                get { return Values; }
            }

	        public void Dispose()
	        {
                ResetCollections();
	        }
	    }

	    private bool _disposed;
		private readonly ReaderWriterLockSlim _disposalLocker = new ReaderWriterLockSlim();

		/// <summary>
		/// Gets a value indicating whether this instance is disposed.
		/// </summary>
		/// <value>
		/// 	<c>true</c> if this instance is disposed; otherwise, <c>false</c>.
		/// </value>
		public bool IsDisposed
		{
			get { return _disposed; }
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
		/// </summary>
		/// <filterpriority>2</filterpriority>
		public void Dispose()
		{
			Dispose(true);

			// Use SupressFinalize in case a subclass of this type implements a finalizer.
			GC.SuppressFinalize(this);
		}

        ~ApplicationEventsResolver()
		{
			// Run dispose but let the class know it was due to the finalizer running.
			Dispose(false);
		}

		private void Dispose(bool disposing)
		{
			// Only operate if we haven't already disposed
			if (IsDisposed || disposing == false) return;

			using (new WriteLock(_disposalLocker))
			{
				// Check again now we're inside the lock
				if (IsDisposed) return;

				// Call to actually release resources. This method is only
				// kept separate so that the entire disposal logic can be used as a VS snippet
				DisposeResources();

				// Indicate that the instance has been disposed.
				_disposed = true;
			}
		}

	    /// <summary>
	    /// Clear out all of the instances, we don't want them hanging around and cluttering up memory
	    /// </summary>
	    private void DisposeResources()
	    {
            _legacyResolver.Dispose();
            ResetCollections();
            _orderedAndFiltered.Clear();
	        _orderedAndFiltered = null;            
	    }
    }
}