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
	"errors"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/cli"
	"github.com/spf13/cobra"
)

var currentLogLevel bool

// loglevelCmd represents the loglevel command
var loglevelCmd = &cobra.Command{
	Use:   "loglevel [loglevel]",
	Short: "Set the loglevel of the ziti tunnel",
	Long:  `Allows you to set the log level of ziti tunnel.`,
	Args: func(cmd *cobra.Command, args []string) error {
		if len(args) < 1 && !currentLogLevel {
			return errors.New("requires 1 argument or a flag, examples of the accepted loglevel arguments are trace, info, debug etc")
		}
		return nil

	},
	Run: func(cmd *cobra.Command, args []string) {
		flags := map[string]bool{}
		flags["query"] = currentLogLevel
		cli.SetLogLevel(args, flags)
	},
}

func init() {
	rootCmd.AddCommand(loglevelCmd)

	// Here you will define your flags and configuration settings.

	// Cobra supports Persistent Flags which will work for this command
	// and all subcommands, e.g.:
	loglevelCmd.PersistentFlags().BoolVarP(&currentLogLevel, "query", "q", false, "Query current loglevel")

}
