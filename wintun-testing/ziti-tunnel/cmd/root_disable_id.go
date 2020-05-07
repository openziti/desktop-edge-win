package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	rootCmd.AddCommand(disableIdCmd)
}

var disableIdCmd = &cobra.Command{
	Use:    "disable",
	Short:  "Disables any identity in the running service using the provided fingerprint",
	Hidden: true,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Disables any identity in the running service using the provided fingerprint")
	},
}
