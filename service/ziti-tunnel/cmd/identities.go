/*
Copyright © 2021 NAME HERE <EMAIL ADDRESS>

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/
package cmd

import (
	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/service"
	"github.com/spf13/cobra"
)

var servicesOfID string

// identitiesCmd represents the identities command
var identitiesCmd = &cobra.Command{
	Use:   "identities",
	Short: "Lists identities from ziti-tunnel",
	Long: `View the identities that this user has access to.
The records will be fetched from ziti-tunnel`,
	Run: func(cmd *cobra.Command, args []string) {
		flags := map[string]interface{}{}
		flags["services"] = servicesOfID
		flags["prettyJSON"] = prettyJSON
		service.GetIdentities(args, flags)
	},
}

func init() {
	listCmd.AddCommand(identitiesCmd)

	// Here you will define your flags and configuration settings.

	// Cobra supports Persistent Flags which will work for this command
	// and all subcommands, e.g.:
	// identitiesCmd.PersistentFlags().String("foo", "", "A help for foo")

	// Cobra supports local flags which will only run when this command
	// is called directly, e.g.:
	//identitiesCmd.Flags().BoolP("toggle", "t", false, "Help message for toggle")
	identitiesCmd.Flags().StringVarP(&servicesOfID, "services", "s", "", "Lists all services that belonged to the identity")

}
