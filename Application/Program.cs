using Interfaces;
using Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Utilities;

namespace SampleApp
{
    class Program
    {
        static void Main()
        {
            var entriesComparersTypes = GetTypesThatImplementsInterface(typeof(IEqualityComparer<Entry>));
            var firstComparer = Activator.CreateInstance(entriesComparersTypes.First());

            //Aktualnie dumpy będą zapisywane w plikach
            IDumpsRepository dumpsRepository = new DumpFileRepository();

            var webSiteIntegrationsTypes = GetTypesThatImplementsInterface(typeof(IWebSiteIntegration));

            foreach (var webSiteIntegrationType in webSiteIntegrationsTypes)
            {
                //Poniższa linijka kodu z pomocą refleksji tworzy instancje konkretnej integracji
                var webSiteIngegration = (IWebSiteIntegration)Activator.CreateInstance(
                    webSiteIntegrationType,
                    dumpsRepository,
                    firstComparer);

                //Pobierz wszystkie dane dumpów, jednak bez konkretnych ofert by nie zaśmiecić pamięci
                var oldDumpsDetails = webSiteIngegration.DumpsRepository.GetAllDumpDetails(webSiteIngegration.WebPage);

                //Tu następuje wykonywanie zrzutu ze strony internetowej
                var newDump = webSiteIngegration.GenerateDump();

                foreach(var oldDumpDetails in oldDumpsDetails)
                {
                    //Załaduj całego dumpa z pamięci wraz z ofertami
                    var oldDump = webSiteIngegration.DumpsRepository.GetDump(oldDumpDetails);

                    //Znajdź wszystkie oferty w starych dumpach których nie ma w nowym dumpie...
                    foreach (var closedEntry in oldDump.Entries.Where(_ => _.OfferDetails.IsStillValid == true)
                        .Except(
                            newDump.Entries,
                            webSiteIngegration.EntriesComparer))
                    //...i oznacz je jako niekatualne
                        closedEntry.OfferDetails.IsStillValid = false;

                    //Zapisz zmiany w dumpach do repozytorium
                    webSiteIngegration.DumpsRepository.UpdateDump(oldDump);
                }

                //Zapisz nowy dump do repozytorium
                webSiteIngegration.DumpsRepository.InsertDump(newDump);
            }
        }
        /// <summary>
        /// Poniższa instrukcja znajduje wszystkie klasy które implementują interfejs IWebSiteIntegration
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static IEnumerable<Type> GetTypesThatImplementsInterface(Type interfaceType)
        {
            if (!interfaceType.GetTypeInfo().IsInterface)
                throw new ArgumentException();

            var assemblies = new FileInfo(Uri.UnescapeDataString(new UriBuilder(Assembly.GetExecutingAssembly().CodeBase).Path))
                .Directory.GetFiles().Where(file => file.Extension == ".dll")
                .Select(dll => Assembly.LoadFile(dll.FullName));

            return assemblies.SelectMany(element => element.GetTypes())
                  .Where(type => interfaceType.IsAssignableFrom(type)
                  && type.GetTypeInfo().IsClass);
        }
    }
}
