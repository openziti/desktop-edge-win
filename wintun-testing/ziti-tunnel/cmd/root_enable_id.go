package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(enableIdCmd)
}

var enableIdCmd = &cobra.Command{
	Use:    "enable",
	Short:  "Enables any identity in the running service using the provided fingerprint",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Enables any identity in the running service using the provided fingerprint")
	},
}
