﻿// Copyright © 2011 - Present RealDimensions Software, LLC
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// 
// You may obtain a copy of the License at
// 
// 	http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace chocolatey.infrastructure.app.services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using configuration;
    using domain;
    using infrastructure.commands;
    using results;

    public class AutomaticUninstallerService : IAutomaticUninstallerService
    {
        private readonly IChocolateyPackageInformationService _packageInfoService;

        public AutomaticUninstallerService(IChocolateyPackageInformationService packageInfoService)
        {
            _packageInfoService = packageInfoService;
        }

        public void run(PackageResult packageResult, ChocolateyConfiguration config)
        {
            //todo run autouninstaller every time - to do this you must determine if the install path still exists.

            if (config.Features.AutoUninstaller)
            {
                var pkgInfo = _packageInfoService.get_package_information(packageResult.Package);

                if (pkgInfo.RegistrySnapshot != null)
                {
                    this.Log().Info(" Running AutoUninstaller...");

                    foreach (var key in pkgInfo.RegistrySnapshot.RegistryKeys.or_empty_list_if_null())
                    {
                        this.Log().Debug(() => " Preparing uninstall key '{0}'".format_with(key.UninstallString));
                        // split on " /" and " -" for quite a bit more accuracy
                        IList<string> uninstallArgs = key.UninstallString.to_string().Split(new[] {" /", " -"}, StringSplitOptions.RemoveEmptyEntries).ToList();
                        var uninstallExe = uninstallArgs.DefaultIfEmpty(string.Empty).FirstOrDefault().remove_surrounding_quotes();
                        this.Log().Debug(() => " Uninstaller path is '{0}'".format_with(uninstallExe));
                        uninstallArgs.Remove(uninstallExe);

                        if (!key.HasQuietUninstall)
                        {
                            IInstaller installer = new CustomInstaller();

                            //refactor this to elsewhere
                            switch (key.InstallerType)
                            {
                                case InstallerType.Msi:
                                    installer = new MsiInstaller();
                                    break;
                                case InstallerType.InnoSetup:
                                    installer = new InnoSetupInstaller();
                                    break;
                                case InstallerType.Nsis:
                                    installer = new NsisInstaller();
                                    break;
                                case InstallerType.InstallShield:
                                    installer = new CustomInstaller();
                                    break;
                                default:
                                    // skip
                                    break;
                            }

                            this.Log().Debug(() => " Installer type is '{0}'".format_with(installer.GetType().Name));

                            uninstallArgs.Add(installer.build_uninstall_command_arguments());
                        }

                        this.Log().Debug(() => " Args are '{0}'".format_with(uninstallArgs.join(" ")));

                        var exitCode = CommandExecutor.execute(
                            uninstallExe, uninstallArgs.join(" "), config.CommandExecutionTimeoutSeconds,
                            (s, e) =>
                                {
                                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                                    this.Log().Debug(() => " [AutoUninstaller] {0}".format_with(e.Data));
                                },
                            (s, e) =>
                                {
                                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                                    this.Log().Error(() => " [AutoUninstaller] {0}".format_with(e.Data));
                                });

                        if (exitCode != 0)
                        {
                            Environment.ExitCode = exitCode;
                            string logMessage = " Auto uninstaller failed. Please remove machine installation manually.";
                            this.Log().Error(() => logMessage);
                            packageResult.Messages.Add(new ResultMessage(ResultType.Error, logMessage));
                        }
                        else
                        {
                            this.Log().Info(() => " AutoUninstaller has successfully uninstalled {0} from your machine install".format_with(packageResult.Package.Id));
                        }
                    }
                }
            }
            else
            {
                this.Log().Info(() => "Skipping auto uninstaller due to feature not being enabled.");
            }
        }
    }
}