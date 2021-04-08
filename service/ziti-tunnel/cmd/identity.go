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
	"strings"

	"github.com/openziti/desktop-edge-win/service/ziti-tunnel/cli"

	"github.com/spf13/cobra"
)

// identityCmd represents the identity command
var identityCmd = &cobra.Command{
	Use:   "identity [fingerprint] [on/off]",
	Short: "enable or disable the identity",
	Long: `Enable or disable identity based on the On Off values.
	It accepts finger print of the identity followed by on/off value`,
	Args: func(cmd *cobra.Command, args []string) error {
		if len(args) < 2 {
			return errors.New("requires 2 arguments, usage: identity [fingerprint] [on/off]")
		}
		if len(args[0]) == 40 && isValidArg(args[1]) {
			return nil
		}
		return errors.New("incorrect arguments are passed, usage: identity [fingerprint] [on/off]")
	},
	Run: func(cmd *cobra.Command, args []string) {
		cli.OnOffIdentity(args, nil)
	},
}

func isValidArg(onOff string) bool {
	return (strings.EqualFold(onOff, "on") || strings.EqualFold(onOff, "off"))
}

func init() {
	rootCmd.AddCommand(identityCmd)
}
