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

// feedbackCmd represents the feedback command
var feedbackCmd = &cobra.Command{
	Use:   "feedback",
	Short: "fetch system information and logs from ziti-tunnel",
	Long: `Fetch the system information and logs, zip it and attach it to the email.
User has to send the email to support@netfoundry.io`,
	Run: func(cmd *cobra.Command, args []string) {
		cli.GetFeedback(args, nil)
	},
}

func init() {
	rootCmd.AddCommand(feedbackCmd)
}
