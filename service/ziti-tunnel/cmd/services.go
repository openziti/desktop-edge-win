package cmd

/*
 * Copyright NetFoundry, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

import (
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/cli"
	"github.com/spf13/cobra"
)

// servicesCmd represents the services command
var servicesCmd = &cobra.Command{
	Use:   "services",
	Short: "Lists services from ziti-tunnel",
	Long: `View the services that this user has access to.
	The records will be fetched from ziti-tunnel`,
	Run: func(cmd *cobra.Command, args []string) {
		flags := map[string]bool{}
		flags["prettyJSON"] = prettyJSON
		cli.GetServices(args, flags)
	},
}

func init() {
	listCmd.AddCommand(servicesCmd)

}
