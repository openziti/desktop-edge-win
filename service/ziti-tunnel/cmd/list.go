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
	"github.com/spf13/cobra"
)

var prettyJSON bool

// listCmd represents the list command
var listCmd = &cobra.Command{
	Use:   "list",
	Short: "Lists data from ziti-tunnel",
	Long: `View the records that this user has access to.
For example identities, services etc`,
	Run: func(cmd *cobra.Command, args []string) {
		checkHelp()
	},
}

func init() {
	rootCmd.AddCommand(listCmd)

	// Here you will define your flags and configuration settings.

	// Cobra supports Persistent Flags which will work for this command
	// and all subcommands, e.g.:
	listCmd.PersistentFlags().BoolVarP(&prettyJSON, "json", "j", false, "display data in json format")

	// Cobra supports local flags which will only run when this command
	// is called directly, e.g.:
	//listCmd.Flags().String("identities", "all", "Lists identities from ziti tunnel")
}
