package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	serviceCmd.AddCommand(installCmd)
}

var installCmd = &cobra.Command{
	Use:   "install",
	Short: "Installs the executable as a service.",
	Long:  `When installing as a service using this function make sure not to move the executable. The path to the executing program will be used in the service`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("install this thing as a service")
	},
}
