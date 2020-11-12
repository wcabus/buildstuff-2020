#region copyright
// ******************************************************************
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THE CODE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE CODE OR THE USE OR OTHER DEALINGS IN THE CODE.
// ******************************************************************
#endregion

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;
using Castle.Core.Logging;
using Inventory.Data;
using Inventory.Models;
using Microsoft.Extensions.DependencyInjection;

using Inventory.Services;
using Inventory.ViewModels;
using RuhRoh;
using RuhRoh.Extensions.Microsoft.DependencyInjection;

namespace Inventory
{
    public class ServiceLocator : IDisposable
    {
        static private readonly ConcurrentDictionary<int, ServiceLocator> _serviceLocators = new ConcurrentDictionary<int, ServiceLocator>();

        static private ServiceProvider _rootServiceProvider = null;

        static public void Configure(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<ISettingsService, SettingsService>();
            serviceCollection.AddSingleton<IDataServiceFactory, DataServiceFactory>();
            serviceCollection.AddSingleton<ILookupTables, LookupTables>();
            serviceCollection.AddSingleton<ICustomerService, CustomerService>();
            serviceCollection.AddSingleton<IOrderService, OrderService>();
            serviceCollection.AddSingleton<IOrderItemService, OrderItemService>();
            serviceCollection.AddSingleton<IProductService, ProductService>();

            serviceCollection.AddSingleton<IMessageService, MessageService>();
            serviceCollection.AddSingleton<ILogService, LogService>();
            serviceCollection.AddSingleton<IDialogService, DialogService>();
            serviceCollection.AddSingleton<IFilePickerService, FilePickerService>();
            serviceCollection.AddSingleton<ILoginService, LoginService>();

            serviceCollection.AddScoped<IContextService, ContextService>();
            serviceCollection.AddScoped<INavigationService, NavigationService>();
            serviceCollection.AddScoped<ICommonServices, CommonServices>();

            serviceCollection.AddTransient<LoginViewModel>();

            serviceCollection.AddTransient<ShellViewModel>();
            serviceCollection.AddTransient<MainShellViewModel>();

            serviceCollection.AddTransient<DashboardViewModel>();

            serviceCollection.AddTransient<CustomersViewModel>();
            serviceCollection.AddTransient<CustomerDetailsViewModel>();

            serviceCollection.AddTransient<OrdersViewModel>();
            serviceCollection.AddTransient<OrderDetailsViewModel>();
            serviceCollection.AddTransient<OrderDetailsWithItemsViewModel>();

            serviceCollection.AddTransient<OrderItemsViewModel>();
            serviceCollection.AddTransient<OrderItemDetailsViewModel>();

            serviceCollection.AddTransient<ProductsViewModel>();
            serviceCollection.AddTransient<ProductDetailsViewModel>();

            serviceCollection.AddTransient<AppLogsViewModel>();

            serviceCollection.AddTransient<SettingsViewModel>();
            serviceCollection.AddTransient<ValidateConnectionViewModel>();
            serviceCollection.AddTransient<CreateDatabaseViewModel>();

            AddSomeChaos(serviceCollection);

            _rootServiceProvider = serviceCollection.BuildServiceProvider();
        }

        private static void AddSomeChaos(IServiceCollection serviceCollection)
        {
            // Simulate Windows Hello crashing on us while signing in 
            serviceCollection.AffectSingleton<ILoginService, LoginService>()
                .WhenCalling(x => x.SignInWithWindowsHelloAsync())
                .Throw<Exception>()
                .AfterNCalls(2);

            // Simulate a slow system delivering customer data, like an old CRM system
            serviceCollection.AffectSingleton<ICustomerService, CustomerService>()
                .WhenCalling(x => x.GetCustomersAsync(With.Any<DataRequest<Customer>>()))
                .SlowItDownBy(TimeSpan.FromSeconds(20))
                .EveryNCalls(2);

            // Simulate running out of disk space when attempting to log something in the application
            serviceCollection.AffectSingleton<ILogService, LogService>()
                .WhenCalling(x => x.WriteAsync(With.Any<LogType>(), With.Any<string>(), With.Any<string>(), With.Any<string>(), With.Any<string>()))
                .Throw(new System.IO.IOException("No more disk space!"))
                .AfterNCalls(3);

            // First attempt to change data: just replace every customer (on every second call) with a custom one
            //serviceCollection.AffectSingleton<ICustomerService, CustomerService>()
            //    .WhenCalling(x => x.GetCustomerAsync(With.Any<long>()))
            //    .ReturnsAsync(new CustomerModel
            //    {
            //        FirstName = "Chuck",
            //        LastName = "Norris"
            //    })
            //    .EveryNCalls(2);

            // Transformer method to intercept customer data coming back from the database and change it
            Func<Task<CustomerModel>, Task<CustomerModel>> transformer = async task =>
            {
                var customer = await task;
                if (customer == null)
                {
                    return null;
                }

                customer.FirstName = "Chuck";
                customer.PictureSource = null;
                return customer;
            };

            serviceCollection.AffectSingleton<ICustomerService, CustomerService>()
                .WhenCalling(x => x.GetCustomerAsync(With.Any<long>()))
                .ReturnsAsync<CustomerModel>(t => transformer(t))
                .EveryNCalls(2);
        }

        static public ServiceLocator Current
        {
            get
            {
                int currentViewId = ApplicationView.GetForCurrentView().Id;
                return _serviceLocators.GetOrAdd(currentViewId, key => new ServiceLocator());
            }
        }

        static public void DisposeCurrent()
        {
            int currentViewId = ApplicationView.GetForCurrentView().Id;
            if (_serviceLocators.TryRemove(currentViewId, out ServiceLocator current))
            {
                current.Dispose();
            }
        }

        private IServiceScope _serviceScope = null;

        private ServiceLocator()
        {
            _serviceScope = _rootServiceProvider.CreateScope();
        }

        public T GetService<T>()
        {
            return GetService<T>(true);
        }

        public T GetService<T>(bool isRequired)
        {
            if (isRequired)
            {
                return _serviceScope.ServiceProvider.GetRequiredService<T>();
            }
            return _serviceScope.ServiceProvider.GetService<T>();
        }

        #region Dispose
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_serviceScope != null)
                {
                    _serviceScope.Dispose();
                }
            }
        }
        #endregion
    }
}
