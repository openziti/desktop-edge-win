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

var CIDR string
var AddDns string

// updateCmd represents the update command
var updateCmd = &cobra.Command{
	Use:   "update",
	Short: "Updates data into the ziti-tunnel",
	Long: `update command should be used as a sub command to the main command
	It helps to update the data into the ziti-tunnel.
	eg: ziti-tunnel config update --CIDR 100.64.0.1/10 --AddDns=true`,
	Run: func(cmd *cobra.Command, args []string) {
		flags := map[string]interface{}{}
		flags["CIDR"] = CIDR
		flags["AddDns"] = AddDns
		cli.UpdateConfigIPSubnet(args,flags)
	},
}

func init() {
	configCmd.AddCommand(updateCmd)

	updateCmd.Flags().StringVar(&CIDR, "CIDR", "", "Updates the cidr property in the config file")
	updateCmd.Flags().StringVar(&AddDns, "AddDns", "", "Updates the Add Dns property in the config file")

}
